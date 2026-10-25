package pt.yaku.booru;

import java.util.List;

/** An ordered collection of posts (a comic, a series). {@code postIds} is the reading order. */
public record Pool(long id, String name, String description, String category, int postCount,
                   List<Long> postIds, String booruBaseUrl, String createdAt, String updatedAt) {

    public static final String SERIES = "series";
    public static final String COLLECTION = "collection";

    /** e621 stores names with underscores. */
    public String displayName() {
        return name == null ? "Pool #" + id : name.replace('_', ' ');
    }

    public String key() {
        return booruBaseUrl + "#pool:" + id;
    }

    public boolean isSeries() {
        return SERIES.equalsIgnoreCase(category);
    }
}
