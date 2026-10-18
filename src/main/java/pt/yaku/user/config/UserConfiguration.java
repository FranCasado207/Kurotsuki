package pt.yaku.user.config;

import lombok.Getter;
import lombok.NoArgsConstructor;
import lombok.Setter;
import pt.yaku.client.GelbooruPost;

import java.util.ArrayList;
import java.util.List;

@Getter
@Setter
@NoArgsConstructor
public class UserConfiguration {
    List<BooruConfiguration> booruConfigurations = new ArrayList<>();
    String downloadFolder;
    String cacheFolder;
    String language;
    List<String> favoriteTags = new ArrayList<>();
    List<GelbooruPost> favoritePosts = new ArrayList<>();
    List<FolderData> postFolders = new ArrayList<>();
    List<String> blacklistedTags = new ArrayList<>();

}
