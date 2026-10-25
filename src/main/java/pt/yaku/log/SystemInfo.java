package pt.yaku.log;

import java.io.IOException;
import java.io.InputStream;
import java.util.List;
import java.util.Locale;
import java.util.Properties;

import pt.yaku.user.config.BooruConfiguration;

/** A text summary of the environment for the log archive; contains no keys, passwords or user names. */
public final class SystemInfo {

    private SystemInfo() {
    }

    public static String version() {
        Properties properties = new Properties();
        try (InputStream in = SystemInfo.class.getResourceAsStream("/version.properties")) {
            if (in != null) {
                properties.load(in);
            }
        } catch (IOException e) {
            // reported as unknown
        }
        return properties.getProperty("version", "unknown");
    }

    public static String describe(List<BooruConfiguration> boorus) {
        Runtime runtime = Runtime.getRuntime();
        StringBuilder text = new StringBuilder();
        text.append("Kurotsuki ").append(version()).append('\n');
        text.append("Java ").append(System.getProperty("java.version")).append(" (")
                .append(System.getProperty("java.vendor")).append(")\n");
        text.append("JavaFX ").append(System.getProperty("javafx.runtime.version", "unknown")).append('\n');
        text.append("OS ").append(System.getProperty("os.name")).append(' ').append(System.getProperty("os.version"))
                .append(' ').append(System.getProperty("os.arch")).append('\n');
        text.append("Locale ").append(Locale.getDefault()).append('\n');
        text.append("Processors ").append(runtime.availableProcessors()).append('\n');
        text.append("Memory max/used MB ").append(runtime.maxMemory() >> 20).append('/')
                .append((runtime.totalMemory() - runtime.freeMemory()) >> 20).append('\n');
        text.append("Boorus ").append(boorus == null ? 0 : boorus.size()).append('\n');
        if (boorus != null) {
            for (BooruConfiguration booru : boorus) {
                text.append("  ").append(booru.getEngine()).append(' ').append(hostOf(booru.getBaseUrl())).append('\n');
            }
        }
        return text.toString();
    }

    private static String hostOf(String url) {
        try {
            String host = java.net.URI.create(url.strip()).getHost();
            return host == null ? "?" : host;
        } catch (RuntimeException e) {
            return "?";
        }
    }
}
