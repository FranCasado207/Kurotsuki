package pt.yaku.booru;

import java.util.Collection;
import java.util.List;
import java.util.Map;

public interface BooruClient {

    List<Post> searchPosts(String tags, int page, int limit);

    List<String> suggestTags(String prefix, int limit);

    default Map<String, TagCategory> tagCategories(Collection<String> names) {
        return Map.of();
    }
}
