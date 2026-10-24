package pt.yaku.booru.danbooru;

import lombok.Getter;
import lombok.NoArgsConstructor;
import lombok.Setter;
import pt.yaku.booru.Post;
import pt.yaku.booru.TagCategory;

@Getter
@Setter
@NoArgsConstructor
public class DanbooruPost {
    private long id;
    private int score;
    private String md5;
    private String fileExt;
    private String fileUrl;
    private String largeFileUrl;
    private String previewFileUrl;
    private int imageWidth;
    private int imageHeight;
    private String rating;
    private String source;
    private String tagString;
    private String tagStringArtist;
    private String tagStringCopyright;
    private String tagStringCharacter;
    private String tagStringGeneral;
    private String tagStringMeta;

    public Post toPost(String booruBaseUrl) {
        Post post = new Post();
        post.setId(id);
        post.setScore(score);
        post.setWidth(imageWidth);
        post.setHeight(imageHeight);
        post.setImage(md5 != null && fileExt != null ? md5 + "." + fileExt : null);
        post.setRating(toGelbooruRating(rating));
        post.setSource(source);
        post.setTags(tagString);
        post.addTagCategories(tagStringArtist, TagCategory.ARTIST);
        post.addTagCategories(tagStringCopyright, TagCategory.COPYRIGHT);
        post.addTagCategories(tagStringCharacter, TagCategory.CHARACTER);
        post.addTagCategories(tagStringGeneral, TagCategory.GENERAL);
        post.addTagCategories(tagStringMeta, TagCategory.META);
        post.setPreviewUrl(previewFileUrl);
        post.setFileUrl(fileUrl);
        post.setSampleUrl(largeFileUrl);
        post.setBooruBaseUrl(booruBaseUrl);
        return post;
    }

    private static String toGelbooruRating(String rating) {
        if (rating == null) {
            return null;
        }
        return switch (rating) {
            case "g" -> "general";
            case "s" -> "sensitive";
            case "q" -> "questionable";
            case "e" -> "explicit";
            default -> rating;
        };
    }
}
