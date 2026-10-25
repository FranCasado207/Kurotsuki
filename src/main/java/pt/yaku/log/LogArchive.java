package pt.yaku.log;

import java.io.BufferedOutputStream;
import java.io.ByteArrayInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.util.List;
import java.util.stream.Stream;

import pt.yaku.download.comic.ArchiveWriter;

/** Bundles the log files and a system summary into one tar, ready to attach to a bug report. */
public final class LogArchive {

    public static final String FILE_NAME = "kurotsuki-logs.tar";

    private LogArchive() {
    }

    /** Writes {@code target} (replacing it) with every {@code *.log} in {@code logDir} plus system-info.txt. */
    public static Path write(Path logDir, Path target, String systemInfo) throws IOException {
        AppLog.flush();
        List<Path> logs = List.of();
        if (Files.isDirectory(logDir)) {
            try (Stream<Path> files = Files.list(logDir)) {
                logs = files.filter(file -> file.getFileName().toString().endsWith(".log")).sorted().toList();
            }
        }
        Files.createDirectories(target.toAbsolutePath().getParent());
        Path partial = target.resolveSibling(target.getFileName() + ".part");
        long now = System.currentTimeMillis() / 1000;
        try (OutputStream out = new BufferedOutputStream(Files.newOutputStream(partial))) {
            byte[] info = systemInfo.getBytes(StandardCharsets.UTF_8);
            ArchiveWriter.writeTarEntry(out, "system-info.txt", info.length, now, new ByteArrayInputStream(info));
            for (Path log : logs) {
                try (InputStream in = Files.newInputStream(log)) {
                    ArchiveWriter.writeTarEntry(out, "logs/" + log.getFileName(), Files.size(log), now, in);
                }
            }
            out.write(new byte[1024]);
        }
        Files.move(partial, target, StandardCopyOption.REPLACE_EXISTING);
        return target;
    }
}
