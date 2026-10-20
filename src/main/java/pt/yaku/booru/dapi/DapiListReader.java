package pt.yaku.booru.dapi;

import java.io.IOException;
import java.io.InputStream;
import java.lang.annotation.Annotation;
import java.lang.reflect.ParameterizedType;
import java.lang.reflect.Type;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;

import jakarta.ws.rs.Consumes;
import jakarta.ws.rs.Produces;
import jakarta.ws.rs.core.MediaType;
import jakarta.ws.rs.core.MultivaluedMap;
import jakarta.ws.rs.ext.MessageBodyReader;
import jakarta.ws.rs.ext.Provider;

import com.google.gson.FieldNamingPolicy;
import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonParser;

@Provider
@Consumes("*/*")
@Produces("*/*")
public class DapiListReader<T> implements MessageBodyReader<List<T>> {

    private static final Gson GSON = new GsonBuilder()
            .setFieldNamingPolicy(FieldNamingPolicy.LOWER_CASE_WITH_UNDERSCORES)
            .create();

    private final String rootKey;
    private final Class<T> elementType;

    public DapiListReader(String rootKey, Class<T> elementType) {
        this.rootKey = rootKey;
        this.elementType = elementType;
    }

    @Override
    public boolean isReadable(Class<?> type, Type genericType, Annotation[] annotations, MediaType mediaType) {
        return List.class.equals(type)
                && genericType instanceof ParameterizedType parameterized
                && elementType.equals(parameterized.getActualTypeArguments()[0]);
    }

    @Override
    public List<T> readFrom(Class<List<T>> type, Type genericType, Annotation[] annotations,
                            MediaType mediaType, MultivaluedMap<String, String> httpHeaders,
                            InputStream entityStream) throws IOException {
        String body = new String(entityStream.readAllBytes(), StandardCharsets.UTF_8);
        List<T> result = new ArrayList<>();
        if (body.isBlank()) {
            return result;
        }
        JsonElement root = JsonParser.parseString(body);
        JsonArray items = root.isJsonArray() ? root.getAsJsonArray() : root.getAsJsonObject().getAsJsonArray(rootKey);
        if (items != null) {
            for (JsonElement element : items) {
                result.add(GSON.fromJson(element, elementType));
            }
        }
        return result;
    }
}
