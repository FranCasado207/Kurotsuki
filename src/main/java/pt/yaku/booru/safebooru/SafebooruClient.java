package pt.yaku.booru.safebooru;

import java.io.StringReader;
import java.util.ArrayList;
import java.util.List;

import javax.xml.parsers.DocumentBuilderFactory;

import org.w3c.dom.Document;
import org.w3c.dom.Element;
import org.w3c.dom.NodeList;
import org.xml.sax.InputSource;

import pt.yaku.booru.BooruClient;
import pt.yaku.booru.Post;
import pt.yaku.booru.dapi.ApiPost;

public class SafebooruClient implements BooruClient {

    private final SafebooruApi api;
    private final String baseUrl;

    public SafebooruClient(SafebooruApi api, String baseUrl) {
        this.api = api;
        this.baseUrl = baseUrl;
    }

    @Override
    public List<Post> searchPosts(String tags, int page, int limit) {
        List<ApiPost> posts = api.listPosts("dapi", "post", "index", limit, page, tags, 1);
        return posts.stream().map(post -> post.toPost(baseUrl)).toList();
    }

    @Override
    public List<String> suggestTags(String prefix, int limit) {
        String xml = api.listTags("dapi", "tag", "index", prefix + "%", limit, "count", "DESC");
        List<String> names = new ArrayList<>();
        if (xml == null || xml.isBlank()) {
            return names;
        }
        try {
            Document document = DocumentBuilderFactory.newInstance().newDocumentBuilder()
                    .parse(new InputSource(new StringReader(xml)));
            NodeList tags = document.getElementsByTagName("tag");
            for (int i = 0; i < tags.getLength(); i++) {
                names.add(((Element) tags.item(i)).getAttribute("name"));
            }
        } catch (Exception e) {
            throw new IllegalStateException("Invalid tag response from Safebooru", e);
        }
        return names;
    }
}
