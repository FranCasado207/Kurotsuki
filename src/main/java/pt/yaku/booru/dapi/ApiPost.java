package pt.yaku.booru.dapi;

import lombok.Getter;
import lombok.NoArgsConstructor;
import lombok.Setter;
import pt.yaku.booru.Post;

@Getter
@Setter
@NoArgsConstructor
public class ApiPost {
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

    public Post toPost(String booruBaseUrl) {
        Post post = new Post();
        post.setId(id);
        post.setScore(score);
        post.setWidth(width);
        post.setHeight(height);
        post.setImage(image);
        post.setRating(rating);
        post.setSource(source);
        post.setTags(tags);
        post.setPreviewUrl(previewUrl);
        post.setFileUrl(fileUrl);
        post.setSampleUrl(sampleUrl);
        post.setBooruBaseUrl(booruBaseUrl);
        return post;
    }
}
