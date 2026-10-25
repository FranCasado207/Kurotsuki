package pt.yaku.download;

import java.util.Locale;

/** Metadata file written next to a download so tools such as Hydrus can import the tags. */
public enum SidecarFormat {
    NONE(""),
    /** {@code file.jpg.txt}: one tag per line, the convention Hydrus' "filename tag" importers read. */
    TXT(".txt"),
    /** {@code file.jpg.json}: id, source, rating, score, size, url and the tags with their categories. */
    JSON(".json");

    private final String suffix;

    SidecarFormat(String suffix) {
        this.suffix = suffix;
    }

    public String suffix() {
        return suffix;
    }

    public static SidecarFormat parse(String value) {
        if (value == null) {
            return NONE;
        }
        try {
            return valueOf(value.trim().toUpperCase(Locale.ROOT));
        } catch (IllegalArgumentException e) {
            return NONE;
        }
    }
}
