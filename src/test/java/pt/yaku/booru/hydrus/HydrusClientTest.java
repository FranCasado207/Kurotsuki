package pt.yaku.booru.hydrus;

import java.lang.reflect.Proxy;
import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import pt.yaku.booru.Post;

public class HydrusClientTest {

    private int searches;

    private HydrusApi api() {
        return (HydrusApi) Proxy.newProxyInstance(getClass().getClassLoader(), new Class<?>[] {HydrusApi.class},
                (proxy, method, args) -> {
                    if (method.getName().equals("searchFiles")) {
                        searches++;
                        return "{\"file_ids\":[1,2,3,4]}";
                    }
                    String ids = (String) args[1];
                    StringBuilder metadata = new StringBuilder("{\"metadata\":[");
                    String[] parts = ids.replaceAll("[\\[\\]]", "").split(",");
                    for (int i = 0; i < parts.length; i++) {
                        metadata.append(i > 0 ? "," : "").append("{\"file_id\":").append(parts[i])
                                .append(",\"hash\":\"h").append(parts[i]).append("\",\"ext\":\".png\"}");
                    }
                    return metadata.append("]}").toString();
                });
    }

    @Test
    public void testUrlsDoNotContainTheAccessKey() {
        HydrusClient client = new HydrusClient(api(), "http://127.0.0.1:45869/", "SECRETKEY");
        List<Post> posts = client.searchPosts("cat", 0, 2);
        assertEquals(2, posts.size());
        for (Post post : posts) {
            assertFalse(post.getFileUrl().contains("SECRETKEY"));
            assertFalse(post.getPreviewUrl().contains("SECRETKEY"));
        }
        assertEquals("http://127.0.0.1:45869/get_files/file?file_id=1", posts.get(0).getFileUrl());
    }

    @Test
    public void testFollowingPagesReuseTheSearch() {
        HydrusClient client = new HydrusClient(api(), "http://127.0.0.1:45869", "k");
        assertEquals(2, client.searchPosts("paging", 0, 2).size());
        assertEquals(2, client.searchPosts("paging", 1, 2).size());
        assertEquals(0, client.searchPosts("paging", 2, 2).size());
        assertEquals(1, searches);
    }
}
