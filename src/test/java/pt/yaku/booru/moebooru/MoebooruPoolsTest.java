package pt.yaku.booru.moebooru;

import static org.junit.jupiter.api.Assertions.*;

import java.lang.reflect.Proxy;
import java.util.List;

import org.junit.jupiter.api.Test;

import pt.yaku.booru.Pool;
import pt.yaku.booru.PoolOrder;
import pt.yaku.booru.Post;

public class MoebooruPoolsTest {

    private static final String POOL = """
            {"id":99406,"name":"(C82)_Some_Book","created_at":"2025-08-23T09:35:45.491Z",
             "updated_at":"2025-08-23T10:24:26.932Z","user_id":4,"is_public":true,"post_count":2,
             "description":"book"}""";

    private static String post(long id) {
        return "{\"id\":" + id + ",\"width\":10,\"height\":20,\"rating\":\"s\",\"tags\":\"comic\","
                + "\"preview_url\":\"/p" + id + ".jpg\",\"file_url\":\"https://files/f" + id + ".png\","
                + "\"sample_url\":\"\",\"md5\":\"m" + id + "\",\"file_ext\":\"png\"}";
    }

    private String lastQuery = "unset";

    private MoebooruApi api() {
        return (MoebooruApi) Proxy.newProxyInstance(getClass().getClassLoader(), new Class<?>[] {MoebooruApi.class},
                (proxy, method, args) -> switch (method.getName()) {
                    case "listPools" -> {
                        lastQuery = (String) args[1];
                        yield "[" + POOL + "]";
                    }
                    case "showPool" -> POOL.substring(0, POOL.lastIndexOf('}'))
                            + ",\"posts\":[" + post(5) + "," + post(3) + "]}";
                    default -> "[]";
                });
    }

    @Test
    public void testPoolsComeWithoutCategoryOrPostIdsButWithCounts() {
        MoebooruClient client = new MoebooruClient(api(), "https://yande.re");
        assertTrue(client.supportsPools());
        assertFalse(client.supportsPoolFilters(), "Moebooru cannot filter or sort pools");
        List<Pool> pools = client.searchPools("book", Pool.SERIES, PoolOrder.NAME, 0, 40);
        assertEquals("book", lastQuery);
        assertEquals(1, pools.size());
        assertNull(pools.get(0).category());
        assertEquals(2, pools.get(0).postCount());
        assertTrue(pools.get(0).postIds().isEmpty());
        assertEquals("(C82) Some Book", pools.get(0).displayName());
    }

    @Test
    public void testPoolPostsComeInTheServersOrder() {
        MoebooruClient client = new MoebooruClient(api(), "https://yande.re");
        Pool pool = client.getPool(99406);
        assertEquals(List.of(5L, 3L), pool.postIds());
        List<Post> posts = client.poolPosts(pool);
        assertEquals(List.of(5L, 3L), posts.stream().map(Post::getId).toList());
        assertEquals("https://yande.re/p5.jpg", posts.get(0).getPreviewUrl());
    }

    @Test
    public void testOnlyAnEmptyPageEndsTheListing() {
        assertEquals(1, new MoebooruClient(api(), "https://yande.re").poolPageSize(40));
    }
}
