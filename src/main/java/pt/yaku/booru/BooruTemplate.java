package pt.yaku.booru;

import java.net.URI;

import lombok.Getter;

@Getter
public enum BooruTemplate {
    GELBOORU("Gelbooru", BooruEngine.GELBOORU_FAMILY, null, "https://gelbooru.com", true),
    SAFEBOORU("Safebooru", BooruEngine.GELBOORU_FAMILY, null, "https://safebooru.org", false),
    DANBOORU("Danbooru", BooruEngine.DANBOORU_SOFTWARE, null, "https://danbooru.donmai.us", true),
    YANDERE("yande.re", BooruEngine.MOEBOORU_SHIMMIE, BooruFlavor.MOEBOORU, "https://yande.re", false),
    KONACHAN("Konachan", BooruEngine.MOEBOORU_SHIMMIE, BooruFlavor.MOEBOORU, "https://konachan.com", false),
    E621("e621", BooruEngine.E621, null, "https://e621.net", true);

    private final String label;
    private final BooruEngine engine;
    private final BooruFlavor flavor;
    private final String defaultBaseUrl;
    private final boolean usesCredentials;

    BooruTemplate(String label, BooruEngine engine, BooruFlavor flavor, String defaultBaseUrl,
                  boolean usesCredentials) {
        this.label = label;
        this.engine = engine;
        this.flavor = flavor;
        this.defaultBaseUrl = defaultBaseUrl;
        this.usesCredentials = usesCredentials;
    }

    public static String refererFor(String url) {
        String host = URI.create(url).getHost();
        for (BooruTemplate template : values()) {
            String domain = URI.create(template.defaultBaseUrl).getHost();
            if (host != null && (host.equals(domain) || host.endsWith("." + domain))) {
                return template.defaultBaseUrl + "/";
            }
        }
        return URI.create(url).resolve("/").toString();
    }
}
