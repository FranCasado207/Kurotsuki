package pt.yaku.download;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.function.Consumer;
import java.util.function.Supplier;

import javafx.application.Platform;
import javafx.collections.FXCollections;
import javafx.collections.ObservableList;
import pt.yaku.booru.Post;
import pt.yaku.download.DownloadItem.Status;
import pt.yaku.ui.ImageLoader;
import pt.yaku.i18n.Messages;
import lombok.Getter;

public class DownloadQueue {

    private static final int PARALLEL_DOWNLOADS = 2;

    private final Supplier<String> downloadFolder;
    private final Consumer<String> onError;
    @Getter
    private final ObservableList<DownloadItem> items = FXCollections.observableArrayList();
    private final ExecutorService executor = Executors.newFixedThreadPool(PARALLEL_DOWNLOADS, runnable -> {
        Thread thread = new Thread(runnable, "download-queue");
        thread.setDaemon(true);
        return thread;
    });

    public DownloadQueue(Supplier<String> downloadFolder, Consumer<String> onError) {
        this.downloadFolder = downloadFolder;
        this.onError = onError;
    }

    public boolean contains(String key) {
        return items.stream().anyMatch(item -> item.getPost().key().equals(key));
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
                Path target = directory.resolve(fileNameOf(item.getPost()));
                ImageLoader.downloadBlocking(item.getPost().getFileUrl(), target,
                        progress -> Platform.runLater(() -> item.setProgress(progress)));
                Platform.runLater(() -> item.setStatus(Status.DONE));
            } catch (Exception e) {
                Platform.runLater(() -> {
                    item.setMessage(e.getMessage() != null ? e.getMessage() : e.getClass().getSimpleName());
                    item.setStatus(Status.FAILED);
                });
            }
        }, executor);
    }

    private static String fileNameOf(Post post) {
        if (post.getImage() != null && !post.getImage().isBlank()) {
            return post.getImage();
        }
        String url = post.getFileUrl();
        return url.substring(url.lastIndexOf('/') + 1);
    }
}
