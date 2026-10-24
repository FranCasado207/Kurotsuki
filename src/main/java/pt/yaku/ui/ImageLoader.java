package pt.yaku.ui;

import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

import javafx.scene.image.Image;
import pt.yaku.cache.CacheManager;

public final class ImageLoader {

    private final ExecutorService executor = Executors.newFixedThreadPool(6, runnable -> {
        Thread thread = new Thread(runnable, "image-loader");
        thread.setDaemon(true);
        return thread;
    });
    private final CacheManager cache;

    public ImageLoader(CacheManager cache) {
        this.cache = cache;
    }

    public CompletableFuture<Image> load(String url, double size) {
        return cache.fetch(url).thenApplyAsync(file -> {
            Image image = new Image(file.toUri().toString(), size, size, true, true, false);
            return image.isError() ? null : image;
        }, executor).exceptionally(error -> null);
    }
}
