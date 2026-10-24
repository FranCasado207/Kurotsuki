package pt.yaku.booru;

import lombok.Getter;
import lombok.NoArgsConstructor;
import lombok.Setter;

@Getter
@Setter
@NoArgsConstructor
public class Post {
    private long id;
    private int score;
    private int width;
    private int height;
    private String image;
    private String rating;
    private String source;
    private String tags;
    private String previewUrl;
    private String fileUrl;
    private String sampleUrl;
    private String booruBaseUrl;
    private java.util.Map<String, TagCategory> tagCategories = new java.util.LinkedHashMap<>();

    public String key() {
        return booruBaseUrl + "#" + id;
    }

    public void addTagCategories(String tagString, TagCategory category) {
        if (tagString == null || tagString.isBlank()) {
            return;
        }
        if (tagCategories == null) {
            tagCategories = new java.util.LinkedHashMap<>();
        }
        for (String tag : tagString.trim().split("\\s+")) {
            tagCategories.put(tag, category);
        }
    }

    public TagCategory categoryOf(String tag) {
        if (tagCategories == null) {
            return TagCategory.GENERAL;
        }
        return tagCategories.getOrDefault(tag, TagCategory.GENERAL);
    }
}
