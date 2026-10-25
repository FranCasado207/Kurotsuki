package pt.yaku.download;

import java.io.IOException;
import java.net.URI;
import java.sql.SQLException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Collection;
import java.util.HashSet;
import java.util.List;
import java.util.Set;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicLong;
import java.util.function.DoubleConsumer;
import java.util.function.Consumer;
import java.util.function.Function;
import java.util.function.Supplier;

import javafx.application.Platform;
import javafx.collections.FXCollections;
import javafx.collections.ListChangeListener;
import javafx.collections.ObservableList;
import pt.yaku.booru.Pool;
import pt.yaku.booru.Post;
import pt.yaku.ui.UgoiraGif;
import pt.yaku.download.DownloadItem.Status;
import pt.yaku.client.MediaDownloader;
import pt.yaku.i18n.Messages;
import lombok.Getter;
import com.google.gson.Gson;
import pt.yaku.storage.LibraryStore;

public class DownloadQueue {

    private static final int PARALLEL_DOWNLOADS = 2;
    private static final int MAX_NAME_LENGTH = 200;
    private static final long PROGRESS_INTERVAL_NANOS = 100_000_000L;
    private static final java.util.logging.Logger LOGGER =
            java.util.logging.Logger.getLogger(DownloadQueue.class.getName());
    private static final Gson GSON = new Gson();

    private final MediaDownloader downloader;
    private final PoolArchiver poolArchiver;
    private final Supplier<DownloadSettings> settings;
    private final Function<Post, String> groupOf;
    private final LibraryStore store;
    private final Consumer<String> onError;
    @Getter
    private final ObservableList<DownloadItem> items = FXCollections.observableArrayList();
    private final Set<String> keys = new HashSet<>();
    /** Keys saved to disk before; only touched on the FX thread. */
    private final Set<String> downloaded = new HashSet<>();
    /** Targets being written right now, so two items with the same name never share a ".part" file. */
    private final Set<Path> reserved = new HashSet<>();
    private boolean saveScheduled;
    private final ExecutorService executor = Executors.newFixedThreadPool(PARALLEL_DOWNLOADS, runnable -> {
        Thread thread = new Thread(runnable, "download-queue");
        thread.setDaemon(true);
        return thread;
    });

    /** {@code store} may be null (nothing is remembered then); {@code groupOf} names the tag group of a post. */
    public DownloadQueue(MediaDownloader downloader, PoolArchiver poolArchiver, Supplier<DownloadSettings> settings,
                         Function<Post, String> groupOf, LibraryStore store, Consumer<String> onError) {
        this.downloader = downloader;
        this.poolArchiver = poolArchiver;
        this.settings = settings;
        this.groupOf = groupOf;
        this.store = store;
        this.onError = onError;
        items.addListener((ListChangeListener<DownloadItem>) change -> {
            keys.clear();
            items.forEach(item -> keys.add(item.key()));
            while (change.next()) {
                for (DownloadItem added : change.getAddedSubList()) {
                    added.statusProperty().addListener((obs, was, is) -> saveQueue());
                }
            }
            saveQueue();
        });
        restore();
    }

    public boolean contains(String key) {
        return keys.contains(key);
    }

    public boolean wasDownloaded(String key) {
        return downloaded.contains(key);
    }

    public void toggle(Post post) {
        if (contains(post.key())) {
            items.stream()
                    .filter(item -> item.key().equals(post.key()))
                    .findFirst()
                    .ifPresent(this::remove);
        } else {
            items.add(new DownloadItem(post));
        }
    }

    /** Queues every post that is not queued yet (not started); returns how many were added. */
    public int addAll(Collection<Post> posts) {
        List<DownloadItem> fresh = new ArrayList<>();
        Set<String> seen = new HashSet<>();
        for (Post post : posts) {
            if (!contains(post.key()) && seen.add(post.key())) {
                fresh.add(new DownloadItem(post));
            }
        }
        items.addAll(fresh);
        return fresh.size();
    }

    /** Adds the pool to the queue and starts saving it right away. */
    public void downloadPool(Pool pool) {
        if (contains(pool.key())) {
            return;
        }
        DownloadSettings current = settings.get();
        if (!current.hasFolder()) {
            onError.accept(Messages.get("download.folderNotSet"));
            return;
        }
        DownloadItem item = new DownloadItem(pool);
        items.add(item);
        start(item, current);
    }

    public void remove(DownloadItem item) {
        if (item.getStatus() != Status.DOWNLOADING) {
            items.remove(item);
        }
    }

    public void removeFinished() {
        items.removeIf(item -> item.getStatus() == Status.DONE || item.getStatus() == Status.FAILED
                || item.getStatus() == Status.SKIPPED);
    }

    public void startAll() {
        DownloadSettings current = settings.get();
        if (!current.hasFolder()) {
            onError.accept(Messages.get("download.folderNotSet"));
            return;
        }
        for (DownloadItem item : List.copyOf(items)) {
            if (item.getStatus() == Status.QUEUED) {
                start(item, current);
            }
        }
    }

    private void start(DownloadItem item, DownloadSettings current) {
        if (current.skipKnown() && downloaded.contains(item.key())) {
            LOGGER.info("Skipped, already downloaded: " + item.key());
            item.setStatus(Status.SKIPPED);
            return;
        }
        item.setStatus(Status.DOWNLOADING);
        item.setProgress(0);
        Path directory = current.directory();
        // Resolved here, on the FX thread, because the tag groups it may use are UI state.
        String relative = item.getPost() == null ? null
                : new FileNameTemplate(current.template(), groupOf).render(item.getPost());
        CompletableFuture.runAsync(() -> {
            Path target = null;
            try {
                AtomicLong lastUpdate = new AtomicLong();
                DoubleConsumer report = progress -> {
                    long now = System.nanoTime();
                    long previous = lastUpdate.get();
                    if ((progress >= 1.0 || now - previous > PROGRESS_INTERVAL_NANOS)
                            && lastUpdate.compareAndSet(previous, now)) {
                        Platform.runLater(() -> item.setProgress(progress));
                    }
                };
                if (item.getPool() != null) {
                    poolArchiver.archive(item.getPool(), directory, report);
                    finished(item);
                    return;
                }
                Files.createDirectories(directory);
                target = reserveTarget(directory, relative);
                Files.createDirectories(target.getParent());
                downloader.downloadBlocking(item.getPost().getFileUrl(), target, report, true);
                Path saved = target;
                if (UgoiraGif.isUgoiraZip(target)) {
                    Path gif = uniqueTarget(target.getParent(), UgoiraGif.gifNameFor(target).getFileName().toString());
                    UgoiraGif.convert(target, gif);
                    Files.deleteIfExists(target);
                    saved = gif;
                }
                try {
                    Sidecar.write(item.getPost(), saved, current.sidecar());
                } catch (IOException e) {
                    Platform.runLater(() -> onError.accept(Messages.get("download.sidecarError", e.getMessage())));
                }
                finished(item);
            } catch (Exception e) {
                LOGGER.warning("Queue item failed: " + item.key() + " " + e);
                Platform.runLater(() -> {
                    item.setMessage(e.getMessage() != null ? e.getMessage() : e.getClass().getSimpleName());
                    item.setStatus(Status.FAILED);
                });
            } finally {
                release(target);
            }
        }, executor);
    }

    private void finished(DownloadItem item) {
        Platform.runLater(() -> {
            downloaded.add(item.key());
            if (store != null) {
                String key = item.key();
                long now = System.currentTimeMillis();
                store.submit(() -> store.markDownloaded(key, now), e -> onError.accept(e.getMessage()));
            }
            item.setStatus(Status.DONE);
        });
    }

    /** Forgets which posts were saved, so they are downloaded again. */
    public void forgetHistory() {
        downloaded.clear();
        if (store != null) {
            store.submit(store::forgetDownloads, e -> onError.accept(e.getMessage()));
        }
    }

    private Path reserveTarget(Path directory, String relative) throws IOException {
        synchronized (reserved) {
            Path target = uniqueTarget(directory, relative, reserved);
            reserved.add(target);
            return target;
        }
    }

    private void release(Path target) {
        if (target != null) {
            synchronized (reserved) {
                reserved.remove(target);
            }
        }
    }

    private void restore() {
        if (store == null) {
            return;
        }
        try {
            downloaded.addAll(store.downloadedKeys());
            List<DownloadItem> restored = new ArrayList<>();
            for (LibraryStore.QueueEntry entry : store.queueEntries()) {
                try {
                    DownloadItem item = "pool".equals(entry.kind())
                            ? new DownloadItem(GSON.fromJson(entry.payload(), Pool.class))
                            : new DownloadItem(GSON.fromJson(entry.payload(), Post.class));
                    item.setPayload(entry.payload());
                    restored.add(item);
                } catch (RuntimeException e) {
                    // an entry from a different version that no longer parses is dropped
                }
            }
            items.addAll(restored);
        } catch (SQLException e) {
            onError.accept(Messages.get("download.restoreError", e.getMessage()));
        }
    }

    /** Remembers what is still to do (everything except finished items) so a restart can pick it up. */
    private void saveQueue() {
        if (store == null || saveScheduled) {
            return;
        }
        saveScheduled = true;
        try {
            Platform.runLater(this::writeQueue);
        } catch (IllegalStateException e) {
            writeQueue();
        }
    }

    private void writeQueue() {
        saveScheduled = false;
        List<LibraryStore.QueueEntry> entries = new ArrayList<>();
        for (DownloadItem item : items) {
            if (item.getStatus() == Status.DONE || item.getStatus() == Status.SKIPPED) {
                continue;
            }
            if (item.getPayload() == null) {
                item.setPayload(GSON.toJson(item.getPool() != null ? item.getPool() : item.getPost()));
            }
            entries.add(new LibraryStore.QueueEntry(item.key(), item.getPool() != null ? "pool" : "post",
                    item.getPayload()));
        }
        store.submit(() -> store.saveQueue(entries), e -> onError.accept(e.getMessage()));
    }

    /**
     * Builds a file name from data the remote server controls, so it can never point outside the download
     * folder: only the last path segment is used, and separators, control and reserved characters are removed.
     */
    static String safeFileName(Post post) {
        String name = lastSegment(post.getImage());
        if (name.isEmpty()) {
            name = lastSegment(pathOf(post.getFileUrl()));
        }
        return sanitize(name, "post-" + post.getId());
    }

    /** Makes a remote-supplied name safe as a single file name: no separators, reserved characters or leading dots. */
    static String sanitize(String raw, String fallback) {
        String name = lastSegment(raw).replaceAll("[\\p{Cntrl}<>:\"|?*]", "_").strip();
        while (name.startsWith(".")) {
            name = name.substring(1);
        }
        if (name.length() > MAX_NAME_LENGTH) {
            name = name.substring(name.length() - MAX_NAME_LENGTH);
        }
        return name.isEmpty() ? fallback : name;
    }

    static Path uniqueTarget(Path directory, String relative) throws IOException {
        return uniqueTarget(directory, relative, Set.of());
    }

    /**
     * A free path for the relative name (which may contain sub-folders) that is guaranteed to stay inside
     * {@code directory}; a number is added to the name when the file exists or is in {@code reserved}.
     */
    static Path uniqueTarget(Path directory, String relative, Set<Path> reserved) throws IOException {
        Path base = directory.toAbsolutePath().normalize();
        Path target = base.resolve(relative).normalize();
        if (!target.startsWith(base) || target.equals(base)) {
            throw new IOException("Refusing to write outside the download folder: " + relative);
        }
        String fileName = target.getFileName().toString();
        int dot = fileName.lastIndexOf('.');
        String stem = dot > 0 ? fileName.substring(0, dot) : fileName;
        String extension = dot > 0 ? fileName.substring(dot) : "";
        for (int n = 1; Files.exists(target) || reserved.contains(target); n++) {
            target = target.resolveSibling(stem + " (" + n + ")" + extension);
        }
        return target;
    }

    private static String lastSegment(String value) {
        if (value == null) {
            return "";
        }
        int slash = Math.max(value.lastIndexOf('/'), value.lastIndexOf('\\'));
        return value.substring(slash + 1);
    }

    private static String pathOf(String url) {
        if (url == null) {
            return "";
        }
        try {
            String path = URI.create(url).getPath();
            return path == null ? "" : path;
        } catch (IllegalArgumentException e) {
            return "";
        }
    }
}
