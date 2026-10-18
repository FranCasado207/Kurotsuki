package pt.yaku.user.config;

import lombok.Getter;
import lombok.NoArgsConstructor;
import lombok.Setter;
import pt.yaku.client.GelbooruPost;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

@Getter
@Setter
@NoArgsConstructor
public class UserConfiguration {
    List<BooruConfiguration> booruConfigurations = new ArrayList<>();
    String downloadFolder;
    String cacheFolder;
    String language;
    Map<String, String> panelDocks = new HashMap<>();
    Map<String, Double> panelSizes = new HashMap<>();
    List<String> favoriteTags = new ArrayList<>();
    List<GelbooruPost> favoritePosts = new ArrayList<>();
    List<FolderData> postFolders = new ArrayList<>();
    List<String> blacklistedTags = new ArrayList<>();

}
