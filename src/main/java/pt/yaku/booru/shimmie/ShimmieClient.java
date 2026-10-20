package pt.yaku.booru.shimmie;

import java.io.StringReader;
import java.util.ArrayList;
import java.util.List;

import javax.xml.parsers.DocumentBuilderFactory;

import org.w3c.dom.Document;
import org.w3c.dom.Element;
import org.w3c.dom.NodeList;
import org.xml.sax.InputSource;
import pt.yaku.booru.BooruClient;
import pt.yaku.booru.BooruHttp;
import pt.yaku.booru.Post;

public class ShimmieClient implements BooruClient {

    private final ShimmieApi api;
    private final String baseUrl;

    public ShimmieClient(ShimmieApi api, String baseUrl) {
        this.api = api;
        this.baseUrl = baseUrl;
    }

    @Override
    public List<Post> searchPosts(String tags, int page, int limit) {
        String body = api.listPosts(BooruHttp.USER_AGENT, tags, page + 1, limit);
        List<Post> posts = new ArrayList<>();
        if (body == null || body.isBlank()) {
            return posts;
        }
        NodeList nodes = parse(body).getElementsByTagName("post");
        for (int i = 0; i < nodes.getLength(); i++) {
            posts.add(toPost((Element) nodes.item(i)));
        }
        return posts;
    }

    @Override
    public List<String> suggestTags(String prefix, int limit) {
        return List.of();
    }

    private Post toPost(Element element) {
        Post post = new Post();
        post.setId(Long.parseLong(element.getAttribute("id")));
        post.setScore(parseInt(element.getAttribute("score")));
        post.setWidth(parseInt(element.getAttribute("width")));
        post.setHeight(parseInt(element.getAttribute("height")));
        post.setRating(BooruHttp.rating(element.getAttribute("rating")));
        post.setSource(element.getAttribute("source"));
        post.setTags(element.getAttribute("tags"));
        post.setPreviewUrl(BooruHttp.absolute(baseUrl, element.getAttribute("preview_url")));
        post.setFileUrl(BooruHttp.absolute(baseUrl, element.getAttribute("file_url")));
        post.setImage(element.getAttribute("file_name"));
        post.setBooruBaseUrl(baseUrl);
        return post;
    }

    private static int parseInt(String value) {
        try {
            return Integer.parseInt(value);
        } catch (NumberFormatException e) {
            return 0;
        }
    }

    private static Document parse(String body) {
        try {
            return DocumentBuilderFactory.newInstance().newDocumentBuilder()
                    .parse(new InputSource(new StringReader(body)));
        } catch (Exception e) {
            throw new IllegalStateException("Invalid response from Shimmie", e);
        }
    }
}
