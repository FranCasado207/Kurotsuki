package pt.yaku.booru.moebooru;

import java.util.ArrayList;
import java.util.Collection;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import pt.yaku.booru.BooruClient;
import pt.yaku.booru.BooruHttp;
import pt.yaku.booru.Post;
import pt.yaku.booru.TagCategory;

public class MoebooruClient implements BooruClient {

    private final MoebooruApi api;
    private final String baseUrl;

    public MoebooruClient(MoebooruApi api, String baseUrl) {
        this.api = api;
        this.baseUrl = baseUrl;
    }

    @Override
    public List<Post> searchPosts(String tags, int page, int limit) {
        String body = api.listPosts(BooruHttp.USER_AGENT, tags, page + 1, limit);
        List<Post> posts = new ArrayList<>();
        for (JsonElement element : array(body)) {
            posts.add(toPost(element.getAsJsonObject()));
        }
        return posts;
    }

    @Override
    public List<String> suggestTags(String prefix, int limit) {
        String body = api.listTags(BooruHttp.USER_AGENT, prefix + "*", "count", limit);
        List<String> names = new ArrayList<>();
        for (JsonElement element : array(body)) {
            names.add(element.getAsJsonObject().get("name").getAsString());
        }
        return names;
    }

    @Override
    public Map<String, TagCategory> tagCategories(Collection<String> names) {
        Map<String, TagCategory> categories = new HashMap<>();
        for (String name : names) {
            String body = api.listTags(BooruHttp.USER_AGENT, name, "count", 100);
            for (JsonElement element : array(body)) {
                JsonObject tag = element.getAsJsonObject();
                if (name.equals(stringOf(tag, "name"))) {
                    categories.put(name, categoryOf(intOf(tag, "type")));
                    break;
                }
            }
        }
        return categories;
    }

    private static TagCategory categoryOf(int type) {
        return switch (type) {
            case 0 -> TagCategory.GENERAL;
            case 1 -> TagCategory.ARTIST;
            case 3 -> TagCategory.COPYRIGHT;
            case 4 -> TagCategory.CHARACTER;
            default -> TagCategory.OTHER;
        };
    }

    private Post toPost(JsonObject json) {
        Post post = new Post();
        post.setId(json.get("id").getAsLong());
        post.setScore(intOf(json, "score"));
        post.setWidth(intOf(json, "width"));
        post.setHeight(intOf(json, "height"));
        post.setRating(BooruHttp.rating(stringOf(json, "rating")));
        post.setSource(stringOf(json, "source"));
        post.setTags(stringOf(json, "tags"));
        post.setPreviewUrl(BooruHttp.absolute(baseUrl, stringOf(json, "preview_url")));
        post.setFileUrl(BooruHttp.absolute(baseUrl, stringOf(json, "file_url")));
        post.setSampleUrl(BooruHttp.absolute(baseUrl, stringOf(json, "sample_url")));
        String md5 = stringOf(json, "md5");
        String ext = stringOf(json, "file_ext");
        post.setImage(md5 != null && ext != null ? md5 + "." + ext : null);
        post.setBooruBaseUrl(baseUrl);
        return post;
    }

    static JsonArray array(String body) {
        if (body == null || body.isBlank()) {
            return new JsonArray();
        }
        return JsonParser.parseString(body).getAsJsonArray();
    }

    static String stringOf(JsonObject json, String key) {
        return json.has(key) && !json.get(key).isJsonNull() ? json.get(key).getAsString() : null;
    }

    static int intOf(JsonObject json, String key) {
        return json.has(key) && !json.get(key).isJsonNull() ? json.get(key).getAsInt() : 0;
    }
}
