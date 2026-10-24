package pt.yaku.cache;

import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.HexFormat;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.CompletableFuture;
import java.util.function.Supplier;
import java.util.regex.Pattern;
import java.util.stream.Stream;


public final class CacheManager {

    /** Fetches {@code url} into the given file. */
    @FunctionalInterface
    public interface Downloader {
        CompletableFuture<Path> download(String url, Path target);
    }

    private static final Pattern CACHE_FILE = Pattern.compile(
            "[0-9a-f]{64}(\\.part)?|[0-9a-f]{64}\\.[a-z0-9]+(\\.part)?");

    public static final long DEFAULT_MAX_BYTES = 2L << 30;
    private static final long STALE_PART_MILLIS = 60 * 60 * 1000L;

    private final Map<Path, CompletableFuture<Path>> inFlight = new ConcurrentHashMap<>();
    private final Supplier<String> customFolder;
    private final Path defaultFolder;
    private final Downloader downloader;

    public CacheManager(Supplier<String> customFolder, Path defaultFolder, Downloader downloader) {
        this.customFolder = customFolder;
        this.defaultFolder = defaultFolder;
        this.downloader = downloader;
    }

    public Path folder() throws IOException {
        String custom = customFolder.get();
        Path folder = custom != null && !custom.isBlank() ? Path.of(custom) : defaultFolder;
        Files.createDirectories(folder);
        return folder;
    }

    public CompletableFuture<Path> fetch(String url) {
        Path cached;
        try {
            cached = pathFor(url);
        } catch (IOException e) {
            return CompletableFuture.failedFuture(e);
        }
        if (Files.isRegularFile(cached)) {
            return CompletableFuture.completedFuture(cached);
        }
        // Concurrent requests for the same URL share one download instead of racing on the same .part file.
        return inFlight.computeIfAbsent(cached, path -> downloader.download(url, path)
                .whenComplete((result, error) -> inFlight.remove(path)));
    }

    /**
     * Removes stale partial downloads and, if the cache exceeds {@code maxBytes}, the oldest files first.
     */
    public void trim(long maxBytes) throws IOException {
        long staleBefore = System.currentTimeMillis() - STALE_PART_MILLIS;
        List<Path> files;
        try (Stream<Path> stream = cacheFiles()) {
            files = stream.toList();
        }
        List<Path> complete = new ArrayList<>();
        long total = 0;
        for (Path file : files) {
            if (file.getFileName().toString().endsWith(".part")) {
                if (Files.getLastModifiedTime(file).toMillis() < staleBefore) {
                    Files.deleteIfExists(file);
                }
                continue;
            }
            complete.add(file);
            total += Files.size(file);
        }
        complete.sort(Comparator.comparingLong(file -> lastModified(file)));
        for (Path file : complete) {
            if (total <= maxBytes) {
                break;
            }
            long size = Files.size(file);
            if (Files.deleteIfExists(file)) {
                total -= size;
            }
        }
    }

    private static long lastModified(Path file) {
        try {
            return Files.getLastModifiedTime(file).toMillis();
        } catch (IOException e) {
            return 0;
        }
    }

    public long sizeBytes() throws IOException {
        try (Stream<Path> files = cacheFiles()) {
            return files.mapToLong(file -> {
                try {
                    return Files.size(file);
                } catch (IOException e) {
                    return 0;
                }
            }).sum();
        }
    }

    public int clear() throws IOException {
        int deleted = 0;
        try (Stream<Path> files = cacheFiles()) {
            for (Path file : files.toList()) {
                if (Files.deleteIfExists(file)) {
                    deleted++;
                }
            }
        }
        return deleted;
    }

    private Stream<Path> cacheFiles() throws IOException {
        return Files.list(folder())
                .filter(file -> CACHE_FILE.matcher(file.getFileName().toString()).matches())
                .filter(Files::isRegularFile);
    }

    private Path pathFor(String url) throws IOException {
        return folder().resolve(hash(url));
    }

    public static String detectExtension(Path file, String fallback) {
        byte[] header;
        try (InputStream in = Files.newInputStream(file)) {
            header = in.readNBytes(16);
        } catch (IOException e) {
            return fallback;
        }
        if (startsWith(header, 0, 0x89, 'P', 'N', 'G')) {
            return "png";
        }
        if (startsWith(header, 0, 0xFF, 0xD8, 0xFF)) {
            return "jpg";
        }
        if (startsWith(header, 0, 'G', 'I', 'F', '8')) {
            return "gif";
        }
        if (startsWith(header, 0, 'B', 'M')) {
            return "bmp";
        }
        if (startsWith(header, 0, 'P', 'K', 0x03, 0x04)) {
            return "zip";
        }
        if (startsWith(header, 0, 'R', 'I', 'F', 'F') && startsWith(header, 8, 'W', 'E', 'B', 'P')) {
            return "webp";
        }
        if (startsWith(header, 0, 0x1A, 0x45, 0xDF, 0xA3)) {
            return "webm";
        }
        if (startsWith(header, 4, 'f', 't', 'y', 'p')) {
            return startsWith(header, 8, 'q', 't', ' ', ' ') ? "mov" : "mp4";
        }
        return fallback;
    }

    private static boolean startsWith(byte[] data, int offset, int... expected) {
        if (data.length < offset + expected.length) {
            return false;
        }
        for (int i = 0; i < expected.length; i++) {
            if ((data[offset + i] & 0xFF) != expected[i]) {
                return false;
            }
        }
        return true;
    }

    private static String hash(String url) {
        try {
            byte[] digest = MessageDigest.getInstance("SHA-256").digest(url.getBytes(StandardCharsets.UTF_8));
            return HexFormat.of().formatHex(digest);
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException(e);
        }
    }

}
