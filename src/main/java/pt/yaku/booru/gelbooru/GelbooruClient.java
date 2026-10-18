package pt.yaku.booru.gelbooru;

import java.util.List;

import pt.yaku.booru.BooruClient;
import pt.yaku.booru.Post;

public class GelbooruClient implements BooruClient {

    private final GelbooruApi api;
    private final String baseUrl;
    private final String apiKey;
    private final String userId;

    public GelbooruClient(GelbooruApi api, String baseUrl, String apiKey, String userId) {
        this.api = api;
        this.baseUrl = baseUrl;
        this.apiKey = apiKey;
        this.userId = userId;
    }

    @Override
    public List<Post> searchPosts(String tags, int page, int limit) {
        return api.listPosts("dapi", "post", "index", apiKey, userId, limit, page, tags, null, null, 1)
                .stream()
                .map(post -> post.toPost(baseUrl))
                .toList();
    }

    @Override
    public List<String> suggestTags(String prefix, int limit) {
        return api.listTags("dapi", "tag", "index", apiKey, userId, prefix + "%", limit, "count", "DESC", 1)
                .stream()
                .map(tag -> tag.getName())
                .toList();
    }
}
