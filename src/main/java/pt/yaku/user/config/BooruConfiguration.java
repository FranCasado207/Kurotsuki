package pt.yaku.user.config;

import java.net.URI;

import lombok.Getter;
import lombok.NoArgsConstructor;
import lombok.Setter;
import pt.yaku.booru.BooruEngine;
import pt.yaku.booru.BooruFlavor;
import pt.yaku.booru.BooruTemplate;

@Getter
@Setter
@NoArgsConstructor
public class BooruConfiguration {
    BooruTemplate template;
    BooruEngine engine;
    BooruFlavor flavor;
    String customBaseUrl;
    String apiKey;
    String userId;

    public BooruEngine getEngine() {
        return template != null ? template.getEngine() : engine;
    }

    public BooruFlavor getFlavor() {
        return template != null ? template.getFlavor() : flavor;
    }

    public String getBaseUrl() {
        if (customBaseUrl != null) {
            return customBaseUrl;
        }
        return template != null ? template.getDefaultBaseUrl() : null;
    }

    public String getKindLabel() {
        if (template != null) {
            return template.getLabel();
        }
        return engine != null ? engine.getLabel() : "Custom";
    }

    public void validate() {
        String baseUrl = getBaseUrl();
        if (baseUrl == null || baseUrl.isBlank()) {
            throw new IllegalStateException("Booru without baseUrl: " + getKindLabel());
        }
        URI uri;
        try {
            uri = URI.create(baseUrl.strip());
        } catch (IllegalArgumentException e) {
            throw new IllegalStateException("Malformed baseUrl for " + getKindLabel() + ": " + baseUrl);
        }
        String scheme = uri.getScheme();
        if (uri.getHost() == null || scheme == null
                || !(scheme.equalsIgnoreCase("http") || scheme.equalsIgnoreCase("https"))) {
            throw new IllegalStateException("baseUrl of " + getKindLabel() + " must be an http(s) URL: " + baseUrl);
        }
    }

    /** True when credentials are configured for a non-local host reached over unencrypted http. */
    public boolean sendsCredentialsInClear() {
        boolean hasCredentials = (apiKey != null && !apiKey.isBlank()) || (userId != null && !userId.isBlank());
        URI uri = URI.create(getBaseUrl().strip());
        String host = uri.getHost();
        boolean local = "localhost".equalsIgnoreCase(host) || "127.0.0.1".equals(host) || "[::1]".equals(host);
        return hasCredentials && "http".equalsIgnoreCase(uri.getScheme()) && !local;
    }
}
