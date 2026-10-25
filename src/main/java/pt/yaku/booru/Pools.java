package pt.yaku.booru;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

/** Helpers shared by the boorus that implement pools. */
public final class Pools {

    private static final int COVER_BATCH = 40;

    private Pools() {
    }

    /** Parses the pool objects of Danbooru and e621, which share one JSON shape. */
    public static List<Pool> parseList(String body, String booruBaseUrl) {
        List<Pool> pools = new ArrayList<>();
        if (body == null || body.isBlank()) {
            return pools;
        }
        for (JsonElement element : JsonParser.parseString(body).getAsJsonArray()) {
            pools.add(parse(element.getAsJsonObject(), booruBaseUrl));
        }
        return pools;
    }

    public static Pool parseOne(String body, String booruBaseUrl) {
        return body == null || body.isBlank() ? null
                : parse(JsonParser.parseString(body).getAsJsonObject(), booruBaseUrl);
    }

    public static Pool parse(JsonObject json, String booruBaseUrl) {
        List<Long> ids = new ArrayList<>();
        if (json.has("post_ids") && json.get("post_ids").isJsonArray()) {
            for (JsonElement id : json.getAsJsonArray("post_ids")) {
                ids.add(id.getAsLong());
            }
        }
        int count = json.has("post_count") && !json.get("post_count").isJsonNull()
                ? json.get("post_count").getAsInt() : ids.size();
        return new Pool(json.get("id").getAsLong(), string(json, "name"), string(json, "description"),
                string(json, "category"), count, List.copyOf(ids), booruBaseUrl,
                string(json, "created_at"), string(json, "updated_at"));
    }

    public static String string(JsonObject json, String key) {
        return json.has(key) && !json.get(key).isJsonNull() ? json.get(key).getAsString() : null;
    }

    /**
     * Posts of a pool fetched with the {@code pool:ID} search, which pages through the results in no particular
     * order; they are put back in the pool's own order and posts the account cannot see are skipped.
     */
    public static List<Post> orderedFromSearch(BooruClient client, Pool pool, int pageLimit, int maxPages) {
        Map<Long, Post> byId = new HashMap<>();
        int wanted = pool.postIds().size();
        for (int page = 0; page < maxPages && byId.size() < wanted; page++) {
            List<Post> received = client.searchPosts("pool:" + pool.id(), page, pageLimit);
            if (received.isEmpty()) {
                break;
            }
            received.forEach(post -> byId.put(post.getId(), post));
        }
        List<Post> ordered = new ArrayList<>();
        for (long id : pool.postIds()) {
            Post post = byId.get(id);
            if (post != null) {
                ordered.add(post);
            }
        }
        return ordered;
    }

    /**
     * First post of every pool, fetched with a few {@code id:a,b,c} searches (one request per
     * {@value #COVER_BATCH} pools instead of one per pool). Keyed by pool id.
     */
    public static Map<Long, Post> coversFromSearch(BooruClient client, List<Pool> pools) {
        Map<Long, Long> firstPostOf = new java.util.LinkedHashMap<>(); // keeps the query in list order
        for (Pool pool : pools) {
            if (!pool.postIds().isEmpty()) {
                firstPostOf.put(pool.id(), pool.postIds().get(0));
            }
        }
        Map<Long, Post> posts = new HashMap<>();
        List<Long> ids = new ArrayList<>(firstPostOf.values());
        for (int from = 0; from < ids.size(); from += COVER_BATCH) {
            List<Long> chunk = ids.subList(from, Math.min(ids.size(), from + COVER_BATCH));
            String query = "id:" + String.join(",", chunk.stream().map(String::valueOf).toList());
            client.searchPosts(query, 0, chunk.size()).forEach(post -> posts.put(post.getId(), post));
        }
        Map<Long, Post> covers = new HashMap<>();
        firstPostOf.forEach((poolId, postId) -> {
            Post post = posts.get(postId);
            if (post != null) {
                covers.put(poolId, post);
            }
        });
        return covers;
    }
}
