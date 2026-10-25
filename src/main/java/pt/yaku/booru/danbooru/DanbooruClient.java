package pt.yaku.booru.danbooru;

import java.util.List;
import java.util.Map;

import pt.yaku.booru.BooruClient;
import pt.yaku.booru.BooruHttp;
import pt.yaku.booru.Pool;
import pt.yaku.booru.PoolOrder;
import pt.yaku.booru.Pools;
import pt.yaku.booru.Post;

public class DanbooruClient implements BooruClient {

    private static final String USER_AGENT = BooruHttp.USER_AGENT;
    private static final int POOL_PAGE_LIMIT = 200;
    private static final int MAX_POOL_PAGES = 30;

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

    @Override
    public boolean supportsPools() {
        return true;
    }

    @Override
    public List<Pool> searchPools(String nameContains, String category, PoolOrder order, int page, int limit) {
        String matches = nameContains == null || nameContains.isBlank() ? null
                : "*" + nameContains.trim().replace(' ', '_') + "*";
        return Pools.parseList(api.listPools(USER_AGENT, login, apiKey, matches, category,
                (order == null ? PoolOrder.UPDATED : order).getApiValue(), page + 1, limit), baseUrl);
    }

    @Override
    public Pool getPool(long id) {
        return Pools.parseOne(api.getPool(USER_AGENT, login, apiKey, id), baseUrl);
    }

    @Override
    public List<Post> poolPosts(Pool pool) {
        return Pools.orderedFromSearch(this, pool, POOL_PAGE_LIMIT, MAX_POOL_PAGES);
    }

    @Override
    public Map<Long, Post> poolCovers(List<Pool> pools) {
        return Pools.coversFromSearch(this, pools);
    }
}
