package pt.yaku.log;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

public class LogArchiveTest {

    @Test
    public void testTarHoldsLogsAndSystemInfo(@TempDir Path dir) throws Exception {
        Path logs = Files.createDirectories(dir.resolve("logs"));
        Files.writeString(logs.resolve("kurotsuki-0.log"), "line one\n");
        Files.writeString(logs.resolve("other.txt"), "ignored");
        Path tar = LogArchive.write(logs, dir.resolve("out/kurotsuki-logs.tar"), "Kurotsuki test\n");
        Process process = new ProcessBuilder("tar", "-tf", tar.toString()).redirectErrorStream(true).start();
        String listing = new String(process.getInputStream().readAllBytes());
        assertEquals(0, process.waitFor(), listing);
        assertEquals(List.of("system-info.txt", "logs/kurotsuki-0.log"), listing.lines().toList());
        Process extract = new ProcessBuilder("tar", "-xOf", tar.toString(), "logs/kurotsuki-0.log").start();
        assertEquals("line one\n", new String(extract.getInputStream().readAllBytes()));
        assertTrue(Files.notExists(dir.resolve("out/kurotsuki-logs.tar.part")));
    }

    @Test
    public void testSafeUrlDropsQueryAndKeys() {
        assertEquals("host.test/a/b.png", AppLog.safeUrl("https://host.test/a/b.png?api_key=SECRET&x=1"));
        assertEquals("", AppLog.safeUrl(null));
    }
}
