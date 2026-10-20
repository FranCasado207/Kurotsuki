package pt.yaku.booru;

import lombok.Getter;

@Getter
public enum BooruEngine {
    GELBOORU_FAMILY("Gelbooru Family", true),
    DANBOORU_SOFTWARE("Danbooru Software", true),
    MOEBOORU_SHIMMIE("Moebooru / Shimmie Family", true),
    SZURUBOORU("Szurubooru", true),
    E621("e621 / e621ng", true);

    private final String label;
    private final boolean usesCredentials;

    BooruEngine(String label, boolean usesCredentials) {
        this.label = label;
        this.usesCredentials = usesCredentials;
    }

    public boolean isImplemented() {
        return true;
    }
}
