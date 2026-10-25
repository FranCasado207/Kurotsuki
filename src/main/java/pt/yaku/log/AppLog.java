package pt.yaku.log;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Instant;
import java.time.ZoneId;
import java.time.format.DateTimeFormatter;
import java.util.logging.FileHandler;
import java.util.logging.Formatter;
import java.util.logging.Level;
import java.util.logging.LogRecord;
import java.util.logging.Logger;

import pt.yaku.user.config.AppPaths;

/**
 * Writes the application's log to a few rotating files in the data folder ({@code logs/kurotsuki-N.log}), one
 * line per event. Lines carry hosts, counts, sizes and timings, never full URLs, tags, keys or file contents.
 */
public final class AppLog {

    static final int FILE_BYTES = 1_000_000;
    static final int FILE_COUNT = 5;
    private static final DateTimeFormatter TIME =
            DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm:ss.SSS").withZone(ZoneId.systemDefault());

    private static FileHandler handler;

    private AppLog() {
    }

    public static Path directory() {
        return AppPaths.get().data().resolve("logs");
    }

    /** Starts file logging for everything under {@code pt.yaku}; safe to call twice. */
    public static synchronized void install() {
        if (handler != null) {
            return;
        }
        try {
            Path dir = directory();
            Files.createDirectories(dir);
            handler = new FileHandler(dir.resolve("kurotsuki-%g.log").toString(), FILE_BYTES, FILE_COUNT, true);
            handler.setEncoding(StandardCharsets.UTF_8.name());
            handler.setFormatter(new LineFormatter());
            handler.setLevel(Level.INFO);
            Logger root = Logger.getLogger("pt.yaku");
            root.addHandler(handler);
            if (root.getLevel() == null || root.getLevel().intValue() > Level.INFO.intValue()) {
                root.setLevel(Level.INFO);
            }
        } catch (IOException | RuntimeException e) {
            System.err.println("Could not start file logging: " + e.getMessage());
            return;
        }
        Thread.setDefaultUncaughtExceptionHandler((thread, error) ->
                Logger.getLogger("pt.yaku.uncaught").log(Level.SEVERE, "Uncaught exception in " + thread.getName(), error));
    }

    public static synchronized void flush() {
        if (handler != null) {
            handler.flush();
        }
    }

    /** Host and path of a URL without the query string, which may hold keys or tags. */
    public static String safeUrl(String url) {
        if (url == null) {
            return "";
        }
        try {
            java.net.URI uri = java.net.URI.create(url);
            return (uri.getHost() == null ? "" : uri.getHost()) + (uri.getPath() == null ? "" : uri.getPath());
        } catch (IllegalArgumentException e) {
            return "?";
        }
    }

    static final class LineFormatter extends Formatter {
        @Override
        public String format(LogRecord record) {
            StringBuilder line = new StringBuilder()
                    .append(TIME.format(Instant.ofEpochMilli(record.getMillis()))).append(' ')
                    .append(String.format("%-7s", record.getLevel().getName())).append(' ')
                    .append(shortName(record.getLoggerName())).append(": ")
                    .append(formatMessage(record));
            Throwable thrown = record.getThrown();
            if (thrown != null) {
                line.append(" | ").append(thrown);
                for (StackTraceElement frame : thrown.getStackTrace()) {
                    line.append("\n    at ").append(frame);
                    if (line.length() > 8000) {
                        break;
                    }
                }
            }
            return line.append(System.lineSeparator()).toString();
        }

        private static String shortName(String name) {
            if (name == null) {
                return "";
            }
            int dot = name.lastIndexOf('.');
            return dot < 0 ? name : name.substring(dot + 1);
        }
    }
}
