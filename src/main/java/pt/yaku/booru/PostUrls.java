package pt.yaku.booru;

public final class PostUrls {

    private PostUrls() {
    }

    public static String pageUrl(BooruEngine engine, BooruFlavor flavor, String baseUrl, long id) {
        if (engine == null || baseUrl == null) {
            return null;
        }
        String base = baseUrl.endsWith("/") ? baseUrl.substring(0, baseUrl.length() - 1) : baseUrl;
        return switch (engine) {
            case DANBOORU_SOFTWARE, E621 -> base + "/posts/" + id;
            case GELBOORU_FAMILY -> base + "/index.php?page=post&s=view&id=" + id;
            case SZURUBOORU -> base + "/post/" + id;
            case HYDRUS -> null;
            case MOEBOORU_SHIMMIE -> flavor == BooruFlavor.SHIMMIE
                    ? base + "/post/view/" + id
                    : base + "/post/show/" + id;
        };
    }
}
