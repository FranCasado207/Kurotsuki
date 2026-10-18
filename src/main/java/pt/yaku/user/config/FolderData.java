package pt.yaku.user.config;

import java.util.ArrayList;
import java.util.List;

import lombok.Getter;
import lombok.NoArgsConstructor;
import lombok.Setter;
import pt.yaku.client.GelbooruPost;

@Getter
@Setter
@NoArgsConstructor
public class FolderData {
    String name;
    List<GelbooruPost> posts = new ArrayList<>();

    public FolderData(String name, List<GelbooruPost> posts) {
        this.name = name;
        this.posts = posts;
    }
}
