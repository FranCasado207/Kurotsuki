package pt.yaku.booru;

public enum TagCategory {
    ARTIST,
    COPYRIGHT,
    CHARACTER,
    GENERAL,
    META,
    OTHER;

    public static TagCategory fromName(String name) {
        return switch (name) {
            case "artist" -> ARTIST;
            case "copyright" -> COPYRIGHT;
            case "character" -> CHARACTER;
            case "general" -> GENERAL;
            case "meta" -> META;
            default -> OTHER;
        };
    }
}
