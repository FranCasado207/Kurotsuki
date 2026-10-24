package pt.yaku.download;

import java.net.URI;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashSet;
import java.util.Set;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicLong;
import java.util.function.Consumer;
import java.util.function.Supplier;

import javafx.application.Platform;
import javafx.collections.FXCollections;
import javafx.collections.ListChangeListener;
import javafx.collections.ObservableList;
import pt.yaku.booru.Post;
import pt.yaku.ui.UgoiraGif;
import pt.yaku.download.DownloadItem.Status;
import pt.yaku.client.MediaDownloader;
import pt.yaku.i18n.Messages;
import lombok.Getter;

public class DownloadQueue {

    private static final int PARALLEL_DOWNLOADS = 2;
    private static final int MAX_NAME_LENGTH = 200;
    private static final long PROGRESS_INTERVAL_NANOS = 100_000_000L;

    private final MediaDownloader downloader;
    private final Supplier<String> downloadFolder;
    private final Consumer<String> onError;
    @Getter
    private final ObservableList<DownloadItem> items = FXCollections.observableArrayList();
    private final Set<String> keys = new HashSet<>();
    private final ExecutorService executor = Executors.newFixedThreadPool(PARALLEL_DOWNLOADS, runnable -> {
        Thread thread = new Thread(runnable, "download-queue");
        thread.setDaemon(true);
        return thread;
    });

    public DownloadQueue(MediaDownloader downloader, Supplier<String> downloadFolder, Consumer<String> onError) {
        this.downloader = downloader;
        this.downloadFolder = downloadFolder;
        this.onError = onError;
        items.addListener((ListChangeListener<DownloadItem>) change -> {
            keys.clear();
            items.forEach(item -> keys.add(item.getPost().key()));
        });
    }

    public boolean contains(String key) {
        return keys.contains(key);
    }

    public void toggle(Post post) {
        if (contains(post.key())) {
            items.stream()
                    .filter(item -> item.getPost().key().equals(post.key()))
                    .findFirst()
                    .ifPresent(this::remove);
        } else {
            items.add(new DownloadItem(post));
        }
    }

    public void remove(DownloadItem item) {
        if (item.getStatus() != Status.DOWNLOADING) {
            items.remove(item);
        }
    }

    public void removeFinished() {
        items.removeIf(item -> item.getStatus() == Status.DONE || item.getStatus() == Status.FAILED);
    }

    public void startAll() {
        String folder = downloadFolder.get();
        if (folder == null || folder.isBlank()) {
            onError.accept(Messages.get("download.folderNotSet"));
            return;
        }
        Path directory = Path.of(folder);
        for (DownloadItem item : items) {
            if (item.getStatus() == Status.QUEUED) {
                start(item, directory);
            }
        }
    }

    private void start(DownloadItem item, Path directory) {
        item.setStatus(Status.DOWNLOADING);
        item.setProgress(0);
        CompletableFuture.runAsync(() -> {
            try {
                Files.createDirectories(directory);
                Path target = uniqueTarget(directory, safeFileName(item.getPost()));
                AtomicLong lastUpdate = new AtomicLong();
                downloader.downloadBlocking(item.getPost().getFileUrl(), target, progress -> {
                    long now = System.nanoTime();
                    long previous = lastUpdate.get();
                    if ((progress >= 1.0 || now - previous > PROGRESS_INTERVAL_NANOS)
                            && lastUpdate.compareAndSet(previous, now)) {
                        Platform.runLater(() -> item.setProgress(progress));
                    }
                });
                if (UgoiraGif.isUgoiraZip(target)) {
                    Path gif = uniqueTarget(directory, UgoiraGif.gifNameFor(target).getFileName().toString());
                    UgoiraGif.convert(target, gif);
                    Files.deleteIfExists(target);
                }
                Platform.runLater(() -> item.setStatus(Status.DONE));
            } catch (Exception e) {
                Platform.runLater(() -> {
                    item.setMessage(e.getMessage() != null ? e.getMessage() : e.getClass().getSimpleName());
                    item.setStatus(Status.FAILED);
                });
            }
        }, executor);
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
        name = name.replaceAll("[\\p{Cntrl}<>:\"|?*]", "_").strip();
        while (name.startsWith(".")) {
            name = name.substring(1);
        }
        if (name.length() > MAX_NAME_LENGTH) {
            name = name.substring(name.length() - MAX_NAME_LENGTH);
        }
        return name.isEmpty() ? "post-" + post.getId() : name;
    }

    static Path uniqueTarget(Path directory, String fileName) throws java.io.IOException {
        Path base = directory.toAbsolutePath().normalize();
        Path target = base.resolve(fileName).normalize();
        if (!base.equals(target.getParent())) {
            throw new java.io.IOException("Refusing to write outside the download folder: " + fileName);
        }
        int dot = fileName.lastIndexOf('.');
        String stem = dot > 0 ? fileName.substring(0, dot) : fileName;
        String extension = dot > 0 ? fileName.substring(dot) : "";
        for (int n = 1; Files.exists(target); n++) {
            target = base.resolve(stem + " (" + n + ")" + extension);
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
