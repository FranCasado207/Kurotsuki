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
import java.nio.file.StandardOpenOption;
import java.time.Duration;
import java.time.Instant;
import java.time.ZonedDateTime;
import java.time.format.DateTimeFormatter;
import java.time.format.DateTimeParseException;
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
import pt.yaku.log.AppLog;

/** Downloads media files with timeouts, a size cap and a watchdog that aborts transfers that stop sending. */
public final class MediaDownloader {

    private static final java.util.logging.Logger LOGGER =
            java.util.logging.Logger.getLogger(MediaDownloader.class.getName());

    public static final long MAX_DOWNLOAD_BYTES = 1L << 30;
    private static final Duration CONNECT_TIMEOUT = Duration.ofSeconds(15);
    private static final Duration RESPONSE_TIMEOUT = Duration.ofSeconds(60);
    private static final Duration DEFAULT_STALL_LIMIT = Duration.ofSeconds(30);
    private static final Duration DEFAULT_BACKOFF = Duration.ofSeconds(1);
    private static final Duration MAX_BACKOFF = Duration.ofSeconds(60);
    private static final Duration MAX_RETRY_AFTER = Duration.ofMinutes(2);
    private static final int DEFAULT_ATTEMPTS = 4;
    private static final long REQUEST_SPACING_MILLIS = 200;

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
    private final Duration backoffBase;
    private final int maxAttempts;
    private final HostThrottle throttle = new HostThrottle(REQUEST_SPACING_MILLIS);

    public MediaDownloader(RequestAuth auth) {
        this(auth, DEFAULT_STALL_LIMIT);
    }

    /** {@code stallLimit} is how long a transfer may go without receiving data before it is aborted. */
    public MediaDownloader(RequestAuth auth, Duration stallLimit) {
        this(auth, stallLimit, DEFAULT_BACKOFF, DEFAULT_ATTEMPTS);
    }

    /**
     * {@code backoffBase} is the first wait before a retry (doubled each time, unless the server sent
     * {@code Retry-After}); {@code maxAttempts} counts the first try.
     */
    public MediaDownloader(RequestAuth auth, Duration stallLimit, Duration backoffBase, int maxAttempts) {
        this.auth = auth;
        this.stallLimitNanos = stallLimit.toNanos();
        this.backoffBase = backoffBase;
        this.maxAttempts = Math.max(1, maxAttempts);
    }

    /**
     * Saves the url to {@code target}. Rate limits (429), server errors (5xx) and dropped transfers are retried
     * with exponential backoff, honouring {@code Retry-After}. An interrupted transfer keeps its {@code .part}
     * file and continues with an HTTP {@code Range} request, but only when the server gave an ETag or
     * Last-Modified to prove the file has not changed in between.
     */
    public Path downloadBlocking(String url, Path target, DoubleConsumer progress)
            throws IOException, InterruptedException {
        return downloadBlocking(url, target, progress, false);
    }

    /**
     * {@code spaced} adds a short pause between requests to the same host; it is meant for bulk file downloads,
     * not for thumbnails, where it would make a page of results load one image at a time.
     */
    public Path downloadBlocking(String url, Path target, DoubleConsumer progress, boolean spaced)
            throws IOException, InterruptedException {
        Path partial = target.resolveSibling(target.getFileName() + ".part");
        Path meta = target.resolveSibling(target.getFileName() + ".part.meta");
        String host = URI.create(url).getHost();
        Retryable last = null;
        long started = System.nanoTime();
        for (int attempt = 1; attempt <= maxAttempts; attempt++) {
            try {
                if (spaced) {
                    throttle.acquire(host);
                }
                attemptOnce(url, target, partial, meta, progress);
                progress.accept(1.0);
                if (spaced) {
                    LOGGER.info("Downloaded: " + AppLog.safeUrl(url) + " bytes=" + Files.size(target) + " attempts=" + attempt
                            + " ms=" + (System.nanoTime() - started) / 1_000_000);
                }
                return target;
            } catch (Retryable e) {
                last = e;
                LOGGER.warning("Retry " + attempt + "/" + maxAttempts + ": " + AppLog.safeUrl(url) + " " + e.getMessage());
                if (attempt < maxAttempts) {
                    Thread.sleep(e.retryAfter != null ? e.retryAfter.toMillis() : backoff(attempt).toMillis());
                }
            } catch (IOException e) {
                discard(partial, meta);
                LOGGER.warning("Download failed: " + AppLog.safeUrl(url) + " " + e.getMessage());
                throw e;
            }
        }
        LOGGER.warning("Download gave up after " + maxAttempts + " attempts: " + AppLog.safeUrl(url) + " " + last.getMessage());
        throw new IOException(last.getMessage(), last);
    }

    Duration backoff(int attempt) {
        long millis = backoffBase.toMillis() << Math.min(attempt - 1, 10);
        return Duration.ofMillis(Math.min(millis, MAX_BACKOFF.toMillis()));
    }

    /** Seconds or an HTTP date; null when absent, unparsable or already past. */
    static Duration parseRetryAfter(String value, Instant now) {
        if (value == null || value.isBlank()) {
            return null;
        }
        String trimmed = value.trim();
        try {
            return Duration.ofSeconds(Math.max(0, Long.parseLong(trimmed)));
        } catch (NumberFormatException e) {
            try {
                Instant at = ZonedDateTime.parse(trimmed, DateTimeFormatter.RFC_1123_DATE_TIME).toInstant();
                return at.isAfter(now) ? Duration.between(now, at) : Duration.ZERO;
            } catch (DateTimeParseException ignored) {
                return null;
            }
        }
    }

    private void attemptOnce(String url, Path target, Path partial, Path meta, DoubleConsumer progress)
            throws IOException, InterruptedException {
        long offset = 0;
        String validator = Files.exists(partial) && Files.exists(meta) ? Files.readString(meta).strip() : null;
        if (validator != null && !validator.isEmpty()) {
            offset = Files.size(partial);
        }
        if (offset == 0) {
            discard(partial, meta);
        }
        HttpRequest.Builder builder = requestBuilder(url);
        if (offset > 0) {
            builder.header("Range", "bytes=" + offset + "-").header("If-Range", validator);
        }
        HttpResponse<InputStream> response = CLIENT.send(builder.build(), HttpResponse.BodyHandlers.ofInputStream());
        try (InputStream in = response.body()) {
            int status = response.statusCode();
            if (status == 416) {
                discard(partial, meta);
                throw new Retryable("HTTP 416", Duration.ZERO, null);
            }
            if (status == 429 || status >= 500) {
                Duration wait = parseRetryAfter(response.headers().firstValue("retry-after").orElse(null),
                        Instant.now());
                if (wait != null && wait.compareTo(MAX_RETRY_AFTER) > 0) {
                    throw new IOException("HTTP " + status + " (retry after " + wait.toSeconds() + "s)");
                }
                throw new Retryable("HTTP " + status, wait, null);
            }
            if (status != 200 && status != 206) {
                throw new IOException("HTTP " + status);
            }
            boolean resumed = status == 206;
            if (resumed && contentRangeStart(response) != offset) {
                discard(partial, meta);
                throw new Retryable("Unexpected Content-Range", Duration.ZERO, null);
            }
            if (!resumed) {
                offset = 0;
            }
            long length = response.headers().firstValueAsLong("content-length").orElse(-1L);
            long total = length < 0 ? -1L : length + offset;
            if (total > MAX_DOWNLOAD_BYTES) {
                throw new IOException("File too large (" + total + " bytes)");
            }
            String newValidator = response.headers().firstValue("etag")
                    .or(() -> response.headers().firstValue("last-modified")).orElse(null);
            if (newValidator != null && !newValidator.contains("\n") && !newValidator.contains("\r")) {
                Files.writeString(meta, newValidator);
            } else {
                Files.deleteIfExists(meta);
            }
            boolean resumable = newValidator != null;
            try {
                copy(in, partial, offset, total, progress);
            } catch (TooLarge e) {
                throw e;
            } catch (IOException e) {
                if (!resumable) {
                    discard(partial, meta);
                }
                throw new Retryable(e.getMessage() != null ? e.getMessage() : "Transfer interrupted", null, e);
            }
        }
        Files.move(partial, target, StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.ATOMIC_MOVE);
        Files.deleteIfExists(meta);
    }

    private static long contentRangeStart(HttpResponse<?> response) {
        String header = response.headers().firstValue("content-range").orElse("");
        if (!header.startsWith("bytes ")) {
            return -1;
        }
        int dash = header.indexOf('-');
        if (dash < 0) {
            return -1;
        }
        try {
            return Long.parseLong(header.substring(6, dash).trim());
        } catch (NumberFormatException e) {
            return -1;
        }
    }

    private static void discard(Path partial, Path meta) throws IOException {
        Files.deleteIfExists(partial);
        Files.deleteIfExists(meta);
    }

    /** A failure worth trying again; {@code retryAfter} is what the server asked for, if anything. */
    private static final class Retryable extends IOException {
        final transient Duration retryAfter;

        Retryable(String message, Duration retryAfter, Throwable cause) {
            super(message, cause);
            this.retryAfter = retryAfter;
        }
    }

    private static final class TooLarge extends IOException {
        TooLarge() {
            super("File too large");
        }
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

    private HttpRequest.Builder requestBuilder(String url) {
        HttpRequest.Builder builder = HttpRequest.newBuilder(URI.create(url))
                .header("User-Agent", BooruHttp.USER_AGENT)
                .timeout(RESPONSE_TIMEOUT)
                .GET();
        String referer = BooruTemplate.refererFor(url);
        if (referer != null) {
            builder.header("Referer", referer);
        }
        auth.apply(builder, url);
        return builder;
    }

    private void copy(InputStream in, Path partial, long offset, long total, DoubleConsumer progress)
            throws IOException {
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
        long transferred = offset;
        byte[] buffer = new byte[64 * 1024];
        try (OutputStream out = offset > 0
                ? Files.newOutputStream(partial, StandardOpenOption.APPEND)
                : Files.newOutputStream(partial)) {
            int read;
            while ((read = in.read(buffer)) != -1) {
                lastActivity.set(System.nanoTime());
                transferred += read;
                if (transferred > MAX_DOWNLOAD_BYTES) {
                    throw new TooLarge();
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
