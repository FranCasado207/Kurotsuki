package pt.yaku.booru.danbooru;

import static org.junit.jupiter.api.Assertions.*;

import java.lang.reflect.Proxy;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;

import org.junit.jupiter.api.Test;

import pt.yaku.booru.Pool;
import pt.yaku.booru.PoolOrder;
import pt.yaku.booru.Post;

public class DanbooruPoolsTest {

    private static final String POOL = """
            {"id":26125,"name":"Touhou_-_Variants","created_at":"2025-06-16T13:09:31.696-04:00",
             "updated_at":"2026-10-17T15:07:44.533-04:00","description":"d","is_active":true,
             "post_ids":[30,10,20],"category":"collection","post_count":3}""";

    private final List<String> queries = new ArrayList<>();
    private String lastNameMatches;

    private static DanbooruPost post(long id) {
        DanbooruPost post = new DanbooruPost();
        post.setId(id);
        post.setMd5("m" + id);
        post.setFileExt("png");
        post.setFileUrl("https://cdn/f" + id + ".png");
        post.setPreviewFileUrl("https://cdn/p" + id + ".jpg");
        return post;
    }

    private DanbooruApi api() {
        return (DanbooruApi) Proxy.newProxyInstance(getClass().getClassLoader(), new Class<?>[] {DanbooruApi.class},
                (proxy, method, args) -> switch (method.getName()) {
                    case "listPools" -> {
                        lastNameMatches = (String) args[3];
                        yield "[" + POOL + "]";
                    }
                    case "getPool" -> POOL;
                    case "listPosts" -> {
                        String tags = (String) args[3];
                        queries.add(tags);
                        int page = (Integer) args[4];
                        if (tags.startsWith("pool:")) {
                            // Out of pool order on purpose; page 2 is empty.
                            yield page == 1 ? List.of(post(10), post(20), post(30)) : List.of();
                        }
                        yield List.of(post(30), post(10)); // the "id:" batch of covers
                    }
                    default -> List.of();
                });
    }

    private DanbooruClient client() {
        return new DanbooruClient(api(), "https://danbooru.test", null, null);
    }

    @Test
    public void testPoolsAreListedAndParsed() {
        assertTrue(client().supportsPools());
        List<Pool> pools = client().searchPools("touhou variants", Pool.COLLECTION, PoolOrder.SIZE, 0, 40);
        assertEquals(1, pools.size());
        assertEquals("Touhou - Variants", pools.get(0).displayName());
        assertEquals(List.of(30L, 10L, 20L), pools.get(0).postIds());
        assertEquals("*touhou_variants*", lastNameMatches);
        assertEquals("https://danbooru.test#pool:26125", pools.get(0).key());
    }

    @Test
    public void testPoolPostsFollowThePoolOrder() {
        DanbooruClient client = client();
        List<Post> posts = client.poolPosts(client.getPool(26125));
        assertEquals(List.of(30L, 10L, 20L), posts.stream().map(Post::getId).toList());
        assertEquals(List.of("pool:26125"), queries, "everything arrived on the first page");
    }

    @Test
    public void testCoversUseOneBatchedRequest() {
        DanbooruClient client = client();
        Pool pool = client.getPool(26125);
        Pool second = new Pool(2, "x", null, null, 1, List.of(10L), "https://danbooru.test", null, null);
        Pool empty = new Pool(3, "y", null, null, 0, List.of(), "https://danbooru.test", null, null);
        Map<Long, Post> covers = client.poolCovers(List.of(pool, second, empty));
        assertEquals(30L, covers.get(26125L).getId(), "the first post of the pool is its cover");
        assertEquals(10L, covers.get(2L).getId());
        assertFalse(covers.containsKey(3L));
        assertEquals(List.of("id:30,10"), queries);
    }
}
