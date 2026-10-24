package pt.yaku.client;

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.time.Duration;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionException;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.ScheduledFuture;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicLong;
import java.util.function.DoubleConsumer;

import pt.yaku.booru.BooruHttp;
import pt.yaku.booru.BooruTemplate;

/** Downloads media files with timeouts, a size cap and a watchdog that aborts transfers that stop sending. */
public final class MediaDownloader {

    public static final long MAX_DOWNLOAD_BYTES = 1L << 30;
    private static final Duration CONNECT_TIMEOUT = Duration.ofSeconds(15);
    private static final Duration RESPONSE_TIMEOUT = Duration.ofSeconds(60);
    private static final Duration DEFAULT_STALL_LIMIT = Duration.ofSeconds(30);

    private static final HttpClient CLIENT = HttpClient.newBuilder()
            .followRedirects(HttpClient.Redirect.NORMAL)
            .connectTimeout(CONNECT_TIMEOUT)
            .build();

    private static final ScheduledExecutorService WATCHDOG = Executors.newSingleThreadScheduledExecutor(runnable -> {
        Thread thread = new Thread(runnable, "download-watchdog");
        thread.setDaemon(true);
        return thread;
    });

    private final ExecutorService executor = Executors.newFixedThreadPool(6, runnable -> {
        Thread thread = new Thread(runnable, "media-download");
        thread.setDaemon(true);
        return thread;
    });
    private final RequestAuth auth;
    private final long stallLimitNanos;

    public MediaDownloader(RequestAuth auth) {
        this(auth, DEFAULT_STALL_LIMIT);
    }

    /** {@code stallLimit} is how long a transfer may go without receiving data before it is aborted. */
    public MediaDownloader(RequestAuth auth, Duration stallLimit) {
        this.auth = auth;
        this.stallLimitNanos = stallLimit.toNanos();
    }

    public Path downloadBlocking(String url, Path target, DoubleConsumer progress)
            throws IOException, InterruptedException {
        Path partial = target.resolveSibling(target.getFileName() + ".part");
        try {
            HttpResponse<InputStream> response = CLIENT.send(request(url), HttpResponse.BodyHandlers.ofInputStream());
            try (InputStream in = response.body()) {
                if (response.statusCode() != 200) {
                    throw new IOException("HTTP " + response.statusCode());
                }
                long total = response.headers().firstValueAsLong("content-length").orElse(-1L);
                if (total > MAX_DOWNLOAD_BYTES) {
                    throw new IOException("File too large (" + total + " bytes)");
                }
                copy(in, partial, total, progress);
            }
            Files.move(partial, target, StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.ATOMIC_MOVE);
        } finally {
            Files.deleteIfExists(partial);
        }
        progress.accept(1.0);
        return target;
    }

    public CompletableFuture<Path> download(String url, Path target) {
        return CompletableFuture.supplyAsync(() -> {
            try {
                return downloadBlocking(url, target, progress -> { });
            } catch (IOException e) {
                throw new CompletionException(e);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                throw new CompletionException(e);
            }
        }, executor);
    }

    private HttpRequest request(String url) {
        HttpRequest.Builder builder = HttpRequest.newBuilder(URI.create(url))
                .header("User-Agent", BooruHttp.USER_AGENT)
                .timeout(RESPONSE_TIMEOUT)
                .GET();
        String referer = BooruTemplate.refererFor(url);
        if (referer != null) {
            builder.header("Referer", referer);
        }
        auth.apply(builder, url);
        return builder.build();
    }

    private void copy(InputStream in, Path partial, long total, DoubleConsumer progress) throws IOException {
        AtomicLong lastActivity = new AtomicLong(System.nanoTime());
        long checkMillis = Math.max(50, TimeUnit.NANOSECONDS.toMillis(stallLimitNanos) / 4);
        // The request timeout only covers the response headers; this closes the stream if the body stalls.
        ScheduledFuture<?> watchdog = WATCHDOG.scheduleWithFixedDelay(() -> {
            if (System.nanoTime() - lastActivity.get() > stallLimitNanos) {
                try {
                    in.close();
                } catch (IOException e) {
                    // the blocked read fails, which is what we want
                }
            }
        }, checkMillis, checkMillis, TimeUnit.MILLISECONDS);
        long transferred = 0;
        byte[] buffer = new byte[64 * 1024];
        try (OutputStream out = Files.newOutputStream(partial)) {
            int read;
            while ((read = in.read(buffer)) != -1) {
                lastActivity.set(System.nanoTime());
                transferred += read;
                if (transferred > MAX_DOWNLOAD_BYTES) {
                    throw new IOException("File too large");
                }
                out.write(buffer, 0, read);
                if (total > 0) {
                    progress.accept((double) transferred / total);
                }
            }
        } finally {
            watchdog.cancel(false);
        }
    }
}
