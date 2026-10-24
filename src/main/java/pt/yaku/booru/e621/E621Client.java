package pt.yaku.booru.e621;

import java.util.ArrayList;
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

public class E621Client implements BooruClient {

    private final E621Api api;
    private final String baseUrl;
    private final String authorization;

    public E621Client(E621Api api, String baseUrl, String login, String apiKey) {
        this.api = api;
        this.baseUrl = baseUrl;
        this.authorization = BooruHttp.basic(login, apiKey);
    }

    @Override
    public List<Post> searchPosts(String tags, int page, int limit) {
        String body = api.listPosts(BooruHttp.USER_AGENT, authorization, tags, page + 1, limit);
        List<Post> posts = new ArrayList<>();
        if (body == null || body.isBlank()) {
            return posts;
        }
        JsonArray items = JsonParser.parseString(body).getAsJsonObject().getAsJsonArray("posts");
        for (JsonElement element : items) {
            posts.add(toPost(element.getAsJsonObject()));
        }
        return posts;
    }

    @Override
    public List<String> suggestTags(String prefix, int limit) {
        String body = api.listTags(BooruHttp.USER_AGENT, authorization, prefix + "*", "count", limit);
        List<String> names = new ArrayList<>();
        if (body == null || body.isBlank()) {
            return names;
        }
        for (JsonElement element : JsonParser.parseString(body).getAsJsonArray()) {
            names.add(element.getAsJsonObject().get("name").getAsString());
        }
        return names;
    }

    private Post toPost(JsonObject json) {
        Post post = new Post();
        post.setId(json.get("id").getAsLong());
        JsonObject file = json.getAsJsonObject("file");
        post.setWidth(file.get("width").getAsInt());
        post.setHeight(file.get("height").getAsInt());
        post.setImage(file.get("md5").getAsString() + "." + file.get("ext").getAsString());
        post.setFileUrl(urlOf(file));
        post.setPreviewUrl(urlOf(json.getAsJsonObject("preview")));
        post.setSampleUrl(urlOf(json.getAsJsonObject("sample")));
        post.setScore(json.getAsJsonObject("score").get("total").getAsInt());
        post.setRating(BooruHttp.rating(json.get("rating").getAsString()));
        post.setSource(json.getAsJsonArray("sources").isEmpty() ? null : json.getAsJsonArray("sources").get(0).getAsString());
        JsonObject categories = json.getAsJsonObject("tags");
        post.setTags(allTags(categories));
        for (Map.Entry<String, JsonElement> category : categories.entrySet()) {
            StringBuilder names = new StringBuilder();
            for (JsonElement tag : category.getValue().getAsJsonArray()) {
                names.append(tag.getAsString()).append(' ');
            }
            post.addTagCategories(names.toString(), TagCategory.fromName(category.getKey()));
        }
        post.setBooruBaseUrl(baseUrl);
        return post;
    }

    private String urlOf(JsonObject object) {
        if (object == null || !object.has("url") || object.get("url").isJsonNull()) {
            return null;
        }
        return BooruHttp.absolute(baseUrl, object.get("url").getAsString());
    }

    private static String allTags(JsonObject categories) {
        List<String> tags = new ArrayList<>();
        for (Map.Entry<String, JsonElement> category : categories.entrySet()) {
            for (JsonElement tag : category.getValue().getAsJsonArray()) {
                tags.add(tag.getAsString());
            }
        }
        return String.join(" ", tags);
    }
}
