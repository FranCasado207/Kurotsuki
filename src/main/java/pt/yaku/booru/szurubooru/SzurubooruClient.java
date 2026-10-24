package pt.yaku.booru.szurubooru;

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

public class SzurubooruClient implements BooruClient {

    private final SzurubooruApi api;
    private final String baseUrl;
    private final String authorization;

    public SzurubooruClient(SzurubooruApi api, String baseUrl, String username, String token) {
        this.api = api;
        this.baseUrl = baseUrl;
        this.authorization = BooruHttp.token(username, token);
    }

    @Override
    public List<Post> searchPosts(String tags, int page, int limit) {
        String query = tags == null ? "" : tags;
        String body = api.listPosts(BooruHttp.USER_AGENT, authorization, page * limit, limit, query);
        List<Post> posts = new ArrayList<>();
        for (JsonElement element : results(body)) {
            posts.add(toPost(element.getAsJsonObject()));
        }
        return posts;
    }

    @Override
    public List<String> suggestTags(String prefix, int limit) {
        String body = api.listTags(BooruHttp.USER_AGENT, authorization, 0, limit,
                "name:" + prefix + "* sort:usages");
        List<String> names = new ArrayList<>();
        for (JsonElement element : results(body)) {
            JsonArray tagNames = element.getAsJsonObject().getAsJsonArray("names");
            if (!tagNames.isEmpty()) {
                names.add(tagNames.get(0).getAsString());
            }
        }
        return names;
    }

    @Override
    public Map<String, TagCategory> tagCategories(Collection<String> names) {
        Map<String, TagCategory> categories = new HashMap<>();
        for (String name : names) {
            String body = api.listTags(BooruHttp.USER_AGENT, authorization, 0, 5, "name:" + name);
            for (JsonElement element : results(body)) {
                JsonObject tag = element.getAsJsonObject();
                if (tag.has("names") && tag.getAsJsonArray("names").toString().contains("\"" + name + "\"")) {
                    categories.put(name, categoryOf(tag.has("category") ? tag.get("category").getAsString() : "default"));
                    break;
                }
            }
        }
        return categories;
    }

    private static TagCategory categoryOf(String category) {
        return switch (category) {
            case "default" -> TagCategory.GENERAL;
            case "artist" -> TagCategory.ARTIST;
            case "copyright" -> TagCategory.COPYRIGHT;
            case "character" -> TagCategory.CHARACTER;
            case "meta" -> TagCategory.META;
            default -> TagCategory.OTHER;
        };
    }

    private JsonArray results(String body) {
        if (body == null || body.isBlank()) {
            return new JsonArray();
        }
        return JsonParser.parseString(body).getAsJsonObject().getAsJsonArray("results");
    }

    private Post toPost(JsonObject json) {
        Post post = new Post();
        post.setId(json.get("id").getAsLong());
        post.setScore(json.get("score").getAsInt());
        post.setWidth(json.get("canvasWidth").getAsInt());
        post.setHeight(json.get("canvasHeight").getAsInt());
        post.setRating(safety(json.get("safety").getAsString()));
        post.setSource(json.has("source") && !json.get("source").isJsonNull() ? json.get("source").getAsString() : null);
        List<String> tags = new ArrayList<>();
        for (JsonElement tag : json.getAsJsonArray("tags")) {
            tags.add(tag.getAsString());
        }
        post.setTags(String.join(" ", tags));
        post.setPreviewUrl(BooruHttp.absolute(baseUrl, json.get("thumbnailUrl").getAsString()));
        post.setFileUrl(BooruHttp.absolute(baseUrl, json.get("contentUrl").getAsString()));
        post.setBooruBaseUrl(baseUrl);
        return post;
    }

    private static String safety(String value) {
        return switch (value) {
            case "safe" -> "general";
            case "sketchy" -> "questionable";
            case "unsafe" -> "explicit";
            default -> value;
        };
    }
}
