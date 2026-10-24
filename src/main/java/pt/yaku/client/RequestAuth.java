package pt.yaku.client;

import java.net.http.HttpRequest;
import java.util.List;
import java.util.function.Supplier;

import pt.yaku.booru.BooruEngine;
import pt.yaku.user.config.BooruConfiguration;

/**
 * Adds per-booru credentials to raw media requests (thumbnails, files) so that secrets never have to be
 * embedded in the URLs we store, cache-key or log.
 */
public final class RequestAuth {

    static final String HYDRUS_HEADER = "Hydrus-Client-API-Access-Key";

    private final Supplier<List<BooruConfiguration>> boorus;

    public RequestAuth(Supplier<List<BooruConfiguration>> boorus) {
        this.boorus = boorus;
    }

    public void apply(HttpRequest.Builder request, String url) {
        List<BooruConfiguration> configured = boorus.get();
        if (configured == null || url == null) {
            return;
        }
        for (BooruConfiguration booru : configured) {
            String key = booru.getApiKey();
            if (booru.getEngine() == BooruEngine.HYDRUS && key != null && !key.isBlank()
                    && isUnder(url, booru.getBaseUrl())) {
                request.header(HYDRUS_HEADER, key);
                return;
            }
        }
    }

    static boolean isUnder(String url, String baseUrl) {
        if (baseUrl == null) {
            return false;
        }
        String base = baseUrl.endsWith("/") ? baseUrl : baseUrl + "/";
        return url.startsWith(base);
    }
}
