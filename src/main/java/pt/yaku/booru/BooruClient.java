package pt.yaku.booru;

import java.util.List;

public interface BooruClient {

    List<Post> searchPosts(String tags, int page, int limit);

    List<String> suggestTags(String prefix, int limit);
}
