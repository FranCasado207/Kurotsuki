package pt.yaku.user.config;

import static org.junit.jupiter.api.Assertions.*;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Map;
import java.util.stream.Stream;

import org.junit.jupiter.api.Test;

public class AppPathsTest {

    private static final Path HOME = Path.of("/home/u");

    @Test
    public void testLinuxDefaultsFollowXdg() {
        AppPaths paths = AppPaths.resolve("Linux", name -> null, HOME);
        assertEquals(HOME.resolve(".config/kurotsuki"), paths.config());
        assertEquals(HOME.resolve(".local/share/kurotsuki"), paths.data());
        assertEquals(HOME.resolve(".cache/kurotsuki"), paths.cache());
    }

    @Test
    public void testXdgVariablesAreUsedWhenAbsolute() {
        Map<String, String> env = Map.of("XDG_CONFIG_HOME", "/cfg", "XDG_DATA_HOME", "relative/data",
                "XDG_CACHE_HOME", "/cache");
        AppPaths paths = AppPaths.resolve("Linux", env::get, HOME);
        assertEquals(Path.of("/cfg/kurotsuki"), paths.config());
        assertEquals(HOME.resolve(".local/share/kurotsuki"), paths.data());
        assertEquals(Path.of("/cache/kurotsuki"), paths.cache());
    }

    @Test
    public void testOtherSystemsKeepTheSingleFolder() {
        AppPaths paths = AppPaths.resolve("Windows 11", name -> null, HOME);
        assertEquals(HOME.resolve("kurotsuki"), paths.config());
        assertEquals(HOME.resolve("kurotsuki"), paths.data());
    }

    @Test
    public void testLegacyFolderIsMoved() throws IOException {
        Path root = Files.createTempDirectory("kurotsuki-paths");
        try {
            Path legacy = Files.createDirectories(root.resolve("home/kurotsuki"));
            Files.writeString(legacy.resolve("config.json"), "{}");
            Files.writeString(legacy.resolve("kurotsuki.db"), "db");
            Files.createDirectories(legacy.resolve(".cache"));
            Files.writeString(legacy.resolve(".cache/abc"), "c");
            AppPaths target = new AppPaths(root.resolve("config"), root.resolve("data"), root.resolve("cache"));

            AppPaths.migrateLegacy(legacy, target);

            assertEquals("{}", Files.readString(target.config().resolve("config.json")));
            assertEquals("db", Files.readString(target.data().resolve("kurotsuki.db")));
            assertEquals("c", Files.readString(target.cache().resolve("abc")));
            assertFalse(Files.exists(legacy));

            // already migrated: a new legacy folder must not overwrite anything
            Files.createDirectories(legacy);
            Files.writeString(legacy.resolve("config.json"), "old");
            AppPaths.migrateLegacy(legacy, target);
            assertEquals("{}", Files.readString(target.config().resolve("config.json")));
        } finally {
            try (Stream<Path> files = Files.walk(root)) {
                files.sorted(java.util.Comparator.reverseOrder()).forEach(p -> p.toFile().delete());
            }
        }
    }
}
