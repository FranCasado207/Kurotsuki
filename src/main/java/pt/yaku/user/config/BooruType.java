package pt.yaku.user.config;

import java.net.URI;

import lombok.AccessLevel;
import lombok.AllArgsConstructor;
import lombok.Getter;
import pt.yaku.user.config.preconfigured.DanbooruConfiguration;
import pt.yaku.user.config.preconfigured.GelbooruConfiguration;
import pt.yaku.user.config.preconfigured.SafebooruConfiguration;

@Getter
@AllArgsConstructor(access = AccessLevel.PRIVATE)
public enum BooruType {
    GENERIC(null, BooruConfiguration.class),
    GELBOORU("https://gelbooru.com", GelbooruConfiguration.class),
    SAFEBOORU("https://safebooru.org", SafebooruConfiguration.class),
    DANBOORU("https://danbooru.donmai.us", DanbooruConfiguration.class);

    private final String defaultBaseUrl;
    private final Class<? extends BooruConfiguration> configClass;

    public static String refererFor(String url) {
        String host = URI.create(url).getHost();
        for (BooruType type : values()) {
            if (type.defaultBaseUrl != null && host != null) {
                String domain = URI.create(type.defaultBaseUrl).getHost();
                if (host.equals(domain) || host.endsWith("." + domain)) {
                    return type.defaultBaseUrl + "/";
                }
            }
        }
        return URI.create(url).resolve("/").toString();
    }
}
