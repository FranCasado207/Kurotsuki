package pt.yaku.user.config;

import java.lang.reflect.Type;

import com.google.gson.Gson;
import com.google.gson.JsonDeserializationContext;
import com.google.gson.JsonDeserializer;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;

public class BooruConfigurationAdapter implements JsonDeserializer<BooruConfiguration> {

    private static final Gson PLAIN_GSON = new Gson();

    @Override
    public BooruConfiguration deserialize(JsonElement json, Type type, JsonDeserializationContext ctx) {
        JsonObject obj = json.getAsJsonObject();
        BooruType booruType = obj.has("booruType")
                ? ctx.deserialize(obj.get("booruType"), BooruType.class)
                : BooruType.GENERIC;
        if (booruType == null) {
            return null;
        }
        return PLAIN_GSON.fromJson(json, booruType.getConfigClass());
    }
}
