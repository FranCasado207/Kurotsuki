package pt.yaku.user.config;

import java.lang.reflect.Type;

import com.google.gson.Gson;
import com.google.gson.JsonDeserializationContext;
import com.google.gson.JsonDeserializer;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import pt.yaku.booru.BooruTemplate;

public class BooruConfigurationAdapter implements JsonDeserializer<BooruConfiguration> {

    private static final Gson PLAIN_GSON = new Gson();

    @Override
    public BooruConfiguration deserialize(JsonElement json, Type type, JsonDeserializationContext ctx) {
        JsonObject obj = json.getAsJsonObject();
        if (obj.has("template") || obj.has("engine")) {
            return PLAIN_GSON.fromJson(json, BooruConfiguration.class);
        }
        return migrateLegacy(obj);
    }

    private static BooruConfiguration migrateLegacy(JsonObject obj) {
        BooruConfiguration booru = new BooruConfiguration();
        String legacyType = string(obj, "booruType");
        if (legacyType == null) {
            legacyType = "GENERIC";
        }
        switch (legacyType) {
            case "GELBOORU" -> booru.setTemplate(BooruTemplate.GELBOORU);
            case "SAFEBOORU" -> booru.setTemplate(BooruTemplate.SAFEBOORU);
            case "DANBOORU" -> booru.setTemplate(BooruTemplate.DANBOORU);
            default -> {
            }
        }
        booru.setCustomBaseUrl(string(obj, "customBaseUrl"));
        booru.setApiKey(string(obj, "apiKey"));
        booru.setUserId("DANBOORU".equals(legacyType) ? string(obj, "login") : string(obj, "userId"));
        return booru;
    }

    private static String string(JsonObject obj, String key) {
        return obj.has(key) && !obj.get(key).isJsonNull() ? obj.get(key).getAsString() : null;
    }
}
