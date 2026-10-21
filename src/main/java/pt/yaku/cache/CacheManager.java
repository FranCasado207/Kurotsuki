package pt.yaku.cache;

import java.io.IOException;
import java.io.InputStream;
import java.io.UncheckedIOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.HexFormat;
import java.util.concurrent.CompletableFuture;
import java.util.regex.Pattern;
import java.util.stream.Stream;

import pt.yaku.context.ApplicationContext;
import pt.yaku.user.config.ConfigManager;
import pt.yaku.ui.ImageLoader;

public final class CacheManager {

    private static final String DEFAULT_FOLDER_NAME = ".cache";
    private static final Pattern CACHE_FILE = Pattern.compile(
            "[0-9a-f]{64}(\\.part)?|[0-9a-f]{64}\\.[a-z0-9]+(\\.part)?");

    private static ApplicationContext context;

    private CacheManager() {
    }

    public static void init(ApplicationContext applicationContext) {
        context = applicationContext;
    }

    public static Path folder() throws IOException {
        String custom = context.getUserConfig().getCacheFolder();
        Path folder = custom != null && !custom.isBlank()
                ? Path.of(custom)
                : ConfigManager.getFolderPath().resolve(DEFAULT_FOLDER_NAME);
        Files.createDirectories(folder);
        return folder;
    }

    public static CompletableFuture<Path> fetch(String url) {
        Path cached;
        try {
            cached = pathFor(url);
        } catch (IOException e) {
            return CompletableFuture.failedFuture(e);
        }
        if (Files.isRegularFile(cached)) {
            return CompletableFuture.completedFuture(cached);
        }
        Path partial = cached.resolveSibling(cached.getFileName() + ".part");
        return ImageLoader.download(url, partial).thenApply(downloaded -> {
            try {
                Files.move(downloaded, cached, StandardCopyOption.REPLACE_EXISTING,
                        StandardCopyOption.ATOMIC_MOVE);
                return cached;
            } catch (IOException e) {
                throw new UncheckedIOException(e);
            }
        });
    }

    public static long sizeBytes() throws IOException {
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

    public static int clear() throws IOException {
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

    private static Stream<Path> cacheFiles() throws IOException {
        return Files.list(folder())
                .filter(file -> CACHE_FILE.matcher(file.getFileName().toString()).matches())
                .filter(Files::isRegularFile);
    }

    private static Path pathFor(String url) throws IOException {
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
