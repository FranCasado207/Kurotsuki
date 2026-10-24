package pt.yaku.ui;

import java.io.IOException;
import java.net.URI;
import java.nio.file.Path;
import java.util.List;
import java.util.Locale;

public final class SystemOpener {

    private SystemOpener() {
    }

    public static void openUrl(String url) throws IOException {
        requireWebUrl(url);
        run(command(System.getProperty("os.name"), url.strip()));
    }

    public static void openFile(Path file) throws IOException {
        run(command(System.getProperty("os.name"), file.toAbsolutePath().toString()));
    }

    /** URLs usually come from remote post data, so only plain web links may reach the OS opener. */
    static void requireWebUrl(String url) {
        String scheme = url == null ? null : URI.create(url.strip()).getScheme();
        if (scheme == null || !(scheme.equalsIgnoreCase("http") || scheme.equalsIgnoreCase("https"))) {
            throw new IllegalArgumentException("Only http(s) links can be opened");
        }
    }

    public static List<String> command(String osName, String target) {
        String os = osName == null ? "" : osName.toLowerCase(Locale.ROOT);
        if (os.contains("win")) {
            return List.of("rundll32", "url.dll,FileProtocolHandler", target);
        }
        if (os.contains("mac")) {
            return List.of("open", target);
        }
        return List.of("xdg-open", target);
    }

    private static void run(List<String> command) throws IOException {
        new ProcessBuilder(command).redirectErrorStream(true).start();
    }
}
