package pt.yaku.booru.gelbooru;

import java.io.StringReader;
import java.util.ArrayList;
import java.util.Collection;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

import javax.xml.parsers.DocumentBuilderFactory;

import org.w3c.dom.Document;
import org.w3c.dom.Element;
import org.w3c.dom.NodeList;
import org.xml.sax.InputSource;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import pt.yaku.booru.BooruClient;
import pt.yaku.booru.Post;
import pt.yaku.booru.TagCategory;

public class GelbooruFamilyClient implements BooruClient {

    private final GelbooruFamilyApi api;
    private final String baseUrl;
    private final String apiKey;
    private final String userId;

    public GelbooruFamilyClient(GelbooruFamilyApi api, String baseUrl, String apiKey, String userId) {
        this.api = api;
        this.baseUrl = baseUrl;
        this.apiKey = apiKey;
        this.userId = userId;
    }

    @Override
    public List<Post> searchPosts(String tags, int page, int limit) {
        return api.listPosts("dapi", "post", "index", apiKey, userId, limit, page, tags, null, null, 1)
                .stream()
                .map(post -> post.toPost(baseUrl))
                .toList();
    }

    @Override
    public List<String> suggestTags(String prefix, int limit) {
        String body = api.listTags("dapi", "tag", "index", apiKey, userId, prefix + "%", limit, "count", "DESC", 1);
        if (body == null || body.isBlank()) {
            return List.of();
        }
        return body.trim().startsWith("<") ? namesFromXml(body) : namesFromJson(body);
    }

    private static List<String> namesFromJson(String body) {
        JsonElement root = JsonParser.parseString(body);
        JsonArray tags = root.isJsonArray() ? root.getAsJsonArray()
                : root.getAsJsonObject().has("tag") ? root.getAsJsonObject().getAsJsonArray("tag") : null;
        List<String> names = new ArrayList<>();
        if (tags != null) {
            for (JsonElement tag : tags) {
                names.add(tag.getAsJsonObject().get("name").getAsString());
            }
        }
        return names;
    }

    private static List<String> namesFromXml(String body) {
        List<String> names = new ArrayList<>();
        try {
            Document document = DocumentBuilderFactory.newInstance().newDocumentBuilder()
                    .parse(new InputSource(new StringReader(body)));
            NodeList tags = document.getElementsByTagName("tag");
            for (int i = 0; i < tags.getLength(); i++) {
                names.add(((Element) tags.item(i)).getAttribute("name"));
            }
        } catch (Exception e) {
            throw new IllegalStateException("Invalid tag response", e);
        }
        return names;
    }

    @Override
    public Map<String, TagCategory> tagCategories(Collection<String> names) {
        Map<String, TagCategory> categories = new HashMap<>();
        for (String name : names) {
            String body = api.listTags("dapi", "tag", "index", apiKey, userId, name, 10, null, null, 1);
            Map<String, Integer> types = typesByName(body);
            if (types.containsKey(name)) {
                categories.put(name, categoryOf(types.get(name)));
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
            case 5 -> TagCategory.META;
            default -> TagCategory.OTHER;
        };
    }

    private static Map<String, Integer> typesByName(String body) {
        Map<String, Integer> types = new HashMap<>();
        if (body == null || body.isBlank()) {
            return types;
        }
        if (body.trim().startsWith("<")) {
            try {
                Document document = DocumentBuilderFactory.newInstance().newDocumentBuilder()
                        .parse(new InputSource(new StringReader(body)));
                NodeList tags = document.getElementsByTagName("tag");
                for (int i = 0; i < tags.getLength(); i++) {
                    Element tag = (Element) tags.item(i);
                    if (tag.hasAttribute("type")) {
                        types.put(tag.getAttribute("name"), Integer.parseInt(tag.getAttribute("type")));
                    }
                }
            } catch (Exception e) {
                throw new IllegalStateException("Invalid tag response", e);
            }
            return types;
        }
        JsonElement root = JsonParser.parseString(body);
        JsonArray tags = root.isJsonArray() ? root.getAsJsonArray()
                : root.getAsJsonObject().has("tag") ? root.getAsJsonObject().getAsJsonArray("tag") : null;
        if (tags != null) {
            for (JsonElement tag : tags) {
                JsonObject object = tag.getAsJsonObject();
                if (object.has("type")) {
                    types.put(object.get("name").getAsString(), object.get("type").getAsInt());
                }
            }
        }
        return types;
    }
}
