package pt.yaku.booru.hydrus;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import pt.yaku.booru.BooruClient;
import pt.yaku.booru.Post;
import pt.yaku.booru.TagCategory;

public class HydrusClient implements BooruClient {

    private static final int TAG_SERVICE_TYPE_LOCAL = 5;
    private static final int ID_BATCH = 1000;
    private static final Gson GSON = new GsonBuilder().disableHtmlEscaping().create();

    private static final Map<String, JsonArray> ID_CACHE = new LinkedHashMap<>(4, 0.75f, true) {
        @Override
        protected boolean removeEldestEntry(Map.Entry<String, JsonArray> eldest) {
            return size() > 4;
        }
    };

    private final HydrusApi api;
    private final String baseUrl;
    private final String accessKey;

    public HydrusClient(HydrusApi api, String baseUrl, String accessKey) {
        this.api = api;
        this.baseUrl = baseUrl.endsWith("/") ? baseUrl.substring(0, baseUrl.length() - 1) : baseUrl;
        this.accessKey = accessKey;
    }

    @Override
    public List<Post> searchPosts(String tags, int page, int limit) {
        int from = page * limit;
        int upTo = from + limit;
        List<String> query = new ArrayList<>();
        for (String tag : (tags == null ? "" : tags.trim()).split("\\s+")) {
            if (!tag.isBlank()) {
                query.add(tag);
            }
        }
        JsonArray allIds = fileIds(query, from, upTo);
        if (allIds.size() <= from) {
            return List.of();
        }
        JsonArray fileIds = new JsonArray();
        for (int i = from; i < Math.min(allIds.size(), upTo); i++) {
            fileIds.add(allIds.get(i));
        }
        JsonObject metadata = JsonParser.parseString(api.fileMetadata(accessKey, fileIds.toString())).getAsJsonObject();
        List<Post> posts = new ArrayList<>();
        List<String> localTagServices = localTagServiceKeys(metadata);
        for (JsonElement element : metadata.getAsJsonArray("metadata")) {
            posts.add(toPost(element.getAsJsonObject(), localTagServices));
        }
        return posts;
    }

    /**
     * Hydrus can only return "the first N ids", so page N would otherwise cost O(N). Fetch ids in batches and
     * keep the most recent queries so following pages are served without another search.
     */
    private JsonArray fileIds(List<String> query, int from, int upTo) {
        String cacheKey = baseUrl + "|" + String.join(" ", query);
        if (from > 0) {
            JsonArray cached;
            synchronized (ID_CACHE) {
                cached = ID_CACHE.get(cacheKey);
            }
            if (cached != null && (cached.size() >= upTo || cached.size() % ID_BATCH != 0)) {
                return cached;
            }
        }
        int fetchLimit = ((upTo + ID_BATCH - 1) / ID_BATCH) * ID_BATCH;
        List<String> search = new ArrayList<>(query);
        search.add("system:limit=" + fetchLimit);
        JsonObject result = JsonParser.parseString(api.searchFiles(accessKey, GSON.toJson(search))).getAsJsonObject();
        JsonArray ids = result.getAsJsonArray("file_ids");
        if (ids == null) {
            ids = new JsonArray();
        }
        synchronized (ID_CACHE) {
            ID_CACHE.put(cacheKey, ids);
        }
        return ids;
    }

    @Override
    public List<String> suggestTags(String prefix, int limit) {
        return List.of();
    }

    private Post toPost(JsonObject file, List<String> localTagServices) {
        long fileId = file.get("file_id").getAsLong();
        Post post = new Post();
        post.setId(fileId);
        post.setWidth(intOf(file, "width"));
        post.setHeight(intOf(file, "height"));
        String hash = file.get("hash").getAsString();
        String ext = file.has("ext") ? file.get("ext").getAsString().replaceFirst("^\\.", "") : "";
        post.setImage(ext.isEmpty() ? hash : hash + "." + ext);
        post.setPreviewUrl(url("get_files/thumbnail", fileId));
        post.setFileUrl(url("get_files/file", fileId));
        post.setBooruBaseUrl(baseUrl);

        List<String> tags = displayTags(file, localTagServices);
        post.setTags(String.join(" ", tags));
        for (String tag : tags) {
            post.addTagCategories(tag, categoryOf(tag));
        }
        return post;
    }

    private String url(String endpoint, long fileId) {
        // The access key is deliberately not part of the URL: URLs are persisted and used as cache keys.
        // pt.yaku.client.RequestAuth adds it as a header when the file is fetched.
        return baseUrl + "/" + endpoint + "?file_id=" + fileId;
    }

    private static List<String> displayTags(JsonObject file, List<String> localTagServices) {
        Set<String> tags = new LinkedHashSet<>();
        if (!file.has("tags")) {
            return new ArrayList<>(tags);
        }
        JsonObject byService = file.getAsJsonObject("tags");
        for (String serviceKey : localTagServices) {
            if (!byService.has(serviceKey)) {
                continue;
            }
            JsonObject service = byService.getAsJsonObject(serviceKey);
            if (service.has("0") && service.getAsJsonObject("0").has("display_tags")) {
                for (JsonElement tag : service.getAsJsonObject("0").getAsJsonArray("display_tags")) {
                    tags.add(tag.getAsString());
                }
            }
        }
        return new ArrayList<>(tags);
    }

    private static List<String> localTagServiceKeys(JsonObject metadata) {
        List<String> keys = new ArrayList<>();
        if (!metadata.has("services")) {
            return keys;
        }
        JsonElement services = metadata.get("services");
        if (services.isJsonObject()) {
            for (Map.Entry<String, JsonElement> entry : services.getAsJsonObject().entrySet()) {
                JsonObject service = entry.getValue().getAsJsonObject();
                if (service.has("type") && service.get("type").getAsInt() == TAG_SERVICE_TYPE_LOCAL) {
                    keys.add(entry.getKey());
                }
            }
        }
        return keys;
    }

    static TagCategory categoryOf(String tag) {
        int colon = tag.indexOf(':');
        if (colon <= 0) {
            return TagCategory.GENERAL;
        }
        return switch (tag.substring(0, colon).toLowerCase(Locale.ROOT)) {
            case "creator" -> TagCategory.ARTIST;
            case "series" -> TagCategory.COPYRIGHT;
            case "character" -> TagCategory.CHARACTER;
            default -> TagCategory.GENERAL;
        };
    }

    private static int intOf(JsonObject json, String key) {
        return json.has(key) && !json.get(key).isJsonNull() ? json.get(key).getAsInt() : 0;
    }
}
