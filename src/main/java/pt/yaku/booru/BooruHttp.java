package pt.yaku.booru;

import java.nio.charset.StandardCharsets;
import java.util.Base64;

public final class BooruHttp {

    public static final String USER_AGENT = "kurotsuki/0.5 (desktop booru client)";

    private BooruHttp() {
    }

    public static String absolute(String baseUrl, String url) {
        if (url == null || url.startsWith("http://") || url.startsWith("https://")) {
            return url;
        }
        String base = baseUrl.endsWith("/") ? baseUrl : baseUrl + "/";
        return base + url.replaceFirst("^/", "");
    }

    public static String basic(String user, String secret) {
        if (user == null || secret == null) {
            return null;
        }
        return "Basic " + encode(user + ":" + secret);
    }

    public static String token(String user, String token) {
        if (user == null || token == null) {
            return null;
        }
        return "Token " + encode(user + ":" + token);
    }

    public static String rating(String letter) {
        if (letter == null) {
            return null;
        }
        return switch (letter) {
            case "s" -> "general";
            case "q" -> "questionable";
            case "e" -> "explicit";
            default -> letter;
        };
    }

    private static String encode(String value) {
        return Base64.getEncoder().encodeToString(value.getBytes(StandardCharsets.UTF_8));
    }
}
