package pt.yaku.booru.danbooru;

import java.util.List;

import pt.yaku.booru.BooruClient;
import pt.yaku.booru.Post;

public class DanbooruClient implements BooruClient {

    private static final String USER_AGENT = "kurotsuki/0.5 (desktop booru client)";

    private final DanbooruApi api;
    private final String baseUrl;
    private final String login;
    private final String apiKey;

    public DanbooruClient(DanbooruApi api, String baseUrl, String login, String apiKey) {
        this.api = api;
        this.baseUrl = baseUrl;
        this.login = login;
        this.apiKey = apiKey;
    }

    @Override
    public List<Post> searchPosts(String tags, int page, int limit) {
        // Danbooru numera as páginas a partir de 1; a UI usa 0, como o Gelbooru
        return api.listPosts(USER_AGENT, login, apiKey, tags, page + 1, limit)
                .stream()
                .map(post -> post.toPost(baseUrl))
                .toList();
    }

    @Override
    public List<String> suggestTags(String prefix, int limit) {
        return api.listTags(USER_AGENT, login, apiKey, prefix + "*", "count", limit)
                .stream()
                .map(tag -> tag.getName())
                .toList();
    }
}
