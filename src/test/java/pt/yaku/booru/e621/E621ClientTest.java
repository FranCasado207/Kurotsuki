package pt.yaku.booru.e621;

import static org.junit.jupiter.api.Assertions.*;

import java.lang.reflect.Proxy;
import java.util.List;

import org.junit.jupiter.api.Test;

import pt.yaku.booru.Pool;
import pt.yaku.booru.PoolOrder;
import pt.yaku.booru.Post;

public class E621ClientTest {

    private static final String POOL = """
            {"id":7,"name":"My_Comic","description":"d","category":"series","post_count":3,
             "post_ids":[30,10,20],"updated_at":"2026-10-20T10:00:00.000+02:00"}""";

    private static String post(long id) {
        return """
                {"id":%d,"file":{"width":800,"height":1200,"md5":"m%d","ext":"png","url":"https://s/f%d.png"},
                 "preview":{"url":"https://s/p%d.jpg"},"sample":{"url":null},"score":{"total":1},
                 "rating":"s","sources":[],"tags":{"general":["comic"]},"pools":[7]}"""
                .formatted(id, id, id, id);
    }

    private String lastNameMatches;
    private int postRequests;

    private E621Api api() {
        return (E621Api) Proxy.newProxyInstance(getClass().getClassLoader(), new Class<?>[] {E621Api.class},
                (proxy, method, args) -> switch (method.getName()) {
                    case "listPools" -> {
                        lastNameMatches = (String) args[2];
                        yield "[" + POOL + "]";
                    }
                    case "getPool" -> POOL;
                    case "listPosts" -> {
                        postRequests++;
                        int page = (Integer) args[3];
                        // The server returns the posts in a different order than the pool defines.
                        yield page == 1 ? "{\"posts\":[" + post(10) + "," + post(20) + "," + post(30) + "]}"
                                : "{\"posts\":[]}";
                    }
                    default -> "[]";
                });
    }

    @Test
    public void testPoolsAreParsed() {
        E621Client client = new E621Client(api(), "https://e621.test", null, null);
        assertTrue(client.supportsPools());
        List<Pool> pools = client.searchPools("my comic", Pool.SERIES, PoolOrder.NAME, 0, 40);
        assertEquals(1, pools.size());
        Pool pool = pools.get(0);
        assertEquals(7, pool.id());
        assertEquals("My Comic", pool.displayName());
        assertEquals(List.of(30L, 10L, 20L), pool.postIds());
        assertTrue(pool.isSeries());
        assertEquals("*my_comic*", lastNameMatches);
        assertEquals("https://e621.test#pool:7", pool.key());
    }

    @Test
    public void testPoolPostsFollowThePoolOrder() {
        E621Client client = new E621Client(api(), "https://e621.test", null, null);
        Pool pool = client.getPool(7);
        List<Post> posts = client.poolPosts(pool);
        assertEquals(List.of(30L, 10L, 20L), posts.stream().map(Post::getId).toList());
        assertEquals(List.of(7L), posts.get(0).getPoolIds());
        assertEquals(1, postRequests, "all posts arrived on the first page, so no further request is needed");
    }

    @Test
    public void testBlankSearchSendsNoNameFilter() {
        E621Client client = new E621Client(api(), "https://e621.test", null, null);
        client.searchPools("  ", null, PoolOrder.UPDATED, 0, 40);
        assertNull(lastNameMatches);
    }
}
