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

    public String key() {
        return booruBaseUrl + "#" + id;
    }
}
