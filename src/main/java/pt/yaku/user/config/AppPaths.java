package pt.yaku.user.config;

import java.io.IOException;
import java.nio.file.AtomicMoveNotSupportedException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.util.List;
import java.util.Locale;
import java.util.function.UnaryOperator;
import java.util.logging.Level;
import java.util.logging.Logger;
import java.util.stream.Stream;

/**
 * Where Kurotsuki keeps its files. On Linux this follows the XDG base directories; elsewhere everything stays in
 * {@code ~/kurotsuki}. Data written by older versions in {@code ~/kurotsuki} is moved over on first use.
 */
public record AppPaths(Path config, Path data, Path cache) {

    private static final Logger LOGGER = Logger.getLogger(AppPaths.class.getName());
    private static final String APP_FOLDER = "kurotsuki";
    private static final String LEGACY_CACHE_FOLDER = ".cache";
    private static final String DB_FILE = "kurotsuki.db";

    private static volatile AppPaths current;

    public static AppPaths get() {
        AppPaths paths = current;
        if (paths == null) {
            synchronized (AppPaths.class) {
                paths = current;
                if (paths == null) {
                    Path home = Path.of(System.getProperty("user.home"));
                    paths = resolve(System.getProperty("os.name"), System::getenv, home);
                    migrateLegacy(home.resolve(APP_FOLDER), paths);
                    current = paths;
                }
            }
        }
        return paths;
    }

    static AppPaths resolve(String osName, UnaryOperator<String> env, Path home) {
        String os = osName == null ? "" : osName.toLowerCase(Locale.ROOT);
        if (os.contains("win") || os.contains("mac")) {
            Path folder = home.resolve(APP_FOLDER);
            return new AppPaths(folder, folder, folder.resolve(LEGACY_CACHE_FOLDER));
        }
        return new AppPaths(
                xdg(env, "XDG_CONFIG_HOME", home.resolve(".config")).resolve(APP_FOLDER),
                xdg(env, "XDG_DATA_HOME", home.resolve(".local").resolve("share")).resolve(APP_FOLDER),
                xdg(env, "XDG_CACHE_HOME", home.resolve(".cache")).resolve(APP_FOLDER));
    }

    private static Path xdg(UnaryOperator<String> env, String variable, Path fallback) {
        String value = env.apply(variable);
        if (value != null && !value.isBlank()) {
            Path path = Path.of(value);
            if (path.isAbsolute()) { // the spec says relative values must be ignored
                return path;
            }
        }
        return fallback;
    }

    static void migrateLegacy(Path legacy, AppPaths target) {
        if (!Files.isDirectory(legacy) || legacy.equals(target.config()) || Files.exists(target.config().resolve("config.json"))) {
            return;
        }
        try {
            Files.createDirectories(target.config());
            Files.createDirectories(target.data());
            for (String suffix : List.of("", "-journal", "-wal", "-shm")) {
                moveIfPresent(legacy.resolve(DB_FILE + suffix), target.data().resolve(DB_FILE + suffix));
            }
            Path oldCache = legacy.resolve(LEGACY_CACHE_FOLDER);
            if (Files.isDirectory(oldCache) && !target.cache().equals(oldCache) && Files.notExists(target.cache())) {
                Files.createDirectories(target.cache().getParent());
                Files.move(oldCache, target.cache());
            }
            // config.json goes last: its presence in the new location marks the migration as complete.
            moveIfPresent(legacy.resolve("config.json"), target.config().resolve("config.json"));
            try (Stream<Path> rest = Files.list(legacy)) {
                if (rest.findAny().isEmpty()) {
                    Files.delete(legacy);
                }
            }
        } catch (IOException e) {
            LOGGER.log(Level.WARNING, "Could not move data from " + legacy + " to the new locations", e);
        }
    }

    private static void moveIfPresent(Path from, Path to) throws IOException {
        if (Files.exists(from) && Files.notExists(to)) {
            try {
                Files.move(from, to, StandardCopyOption.ATOMIC_MOVE);
            } catch (AtomicMoveNotSupportedException e) { // different file systems
                Files.move(from, to);
            }
        }
    }
}
