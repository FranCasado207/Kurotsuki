package pt.yaku.client;

import lombok.Getter;
import lombok.NoArgsConstructor;
import lombok.Setter;

@Getter
@Setter
@NoArgsConstructor
public class GelbooruPost {
    private long id;
    private String createdAt;
    private int score;
    private int width;
    private int height;
    private String md5;
    private String directory;
    private String image;
    private String rating;
    private String source;
    private long change;
    private String owner;
    private long creatorId;
    private long parentId;
    private int sample;
    private int previewHeight;
    private int previewWidth;
    private String tags;
    private String title;
    private boolean hasNotes;
    private boolean hasComments;
    private String fileUrl;
    private String previewUrl;
    private String sampleUrl;
    private int sampleHeight;
    private int sampleWidth;
    private String status;
    private int postLocked;
    private boolean hasChildren;
    private String booruBaseUrl;
}
