package pt.yaku.ui;

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.io.UncheckedIOException;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.util.concurrent.CompletableFuture;
import java.util.function.DoubleConsumer;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

import javafx.scene.image.Image;
import pt.yaku.cache.CacheManager;
import pt.yaku.booru.BooruTemplate;

public final class ImageLoader {

    private static final String USER_AGENT = "kurotsuki/0.5 (desktop booru client)";

    private static final HttpClient CLIENT = HttpClient.newBuilder()
            .followRedirects(HttpClient.Redirect.NORMAL)
            .build();

    private static final ExecutorService EXECUTOR = Executors.newFixedThreadPool(6, runnable -> {
        Thread thread = new Thread(runnable, "image-loader");
        thread.setDaemon(true);
        return thread;
    });

    private ImageLoader() {
    }

    public static CompletableFuture<Image> load(String url, double size) {
        return CacheManager.fetch(url).thenApplyAsync(file -> {
            Image image = new Image(file.toUri().toString(), size, size, true, true, false);
            return image.isError() ? null : image;
        }, EXECUTOR).exceptionally(error -> null);
    }

    public static Path downloadBlocking(String url, Path target, DoubleConsumer progress)
            throws IOException, InterruptedException {
        HttpRequest request = HttpRequest.newBuilder(URI.create(url))
                .header("Referer", BooruTemplate.refererFor(url))
                .header("User-Agent", USER_AGENT)
                .GET()
                .build();
        HttpResponse<InputStream> response = CLIENT.send(request, HttpResponse.BodyHandlers.ofInputStream());
        if (response.statusCode() != 200) {
            response.body().close();
            throw new IOException("HTTP " + response.statusCode());
        }
        long total = response.headers().firstValueAsLong("content-length").orElse(-1L);

        Path partial = target.resolveSibling(target.getFileName() + ".part");
        long transferred = 0;
        byte[] buffer = new byte[64 * 1024];
        try (InputStream in = response.body(); OutputStream out = Files.newOutputStream(partial)) {
            int read;
            while ((read = in.read(buffer)) != -1) {
                out.write(buffer, 0, read);
                transferred += read;
                if (total > 0) {
                    progress.accept((double) transferred / total);
                }
            }
        }
        Files.move(partial, target, StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.ATOMIC_MOVE);
        progress.accept(1.0);
        return target;
    }

    public static CompletableFuture<Path> download(String url, Path target) {
        return CompletableFuture.supplyAsync(() -> {
            try {
                HttpRequest request = HttpRequest.newBuilder(URI.create(url))
                        .header("Referer", BooruTemplate.refererFor(url))
                        .header("User-Agent", USER_AGENT)
                        .GET()
                        .build();
                HttpResponse<Path> response = CLIENT.send(request, HttpResponse.BodyHandlers.ofFile(target));
                if (response.statusCode() != 200) {
                    throw new IllegalStateException("HTTP " + response.statusCode());
                }
                return target;
            } catch (IOException e) {
                throw new UncheckedIOException(e);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                throw new IllegalStateException(e);
            }
        }, EXECUTOR);
    }
}
