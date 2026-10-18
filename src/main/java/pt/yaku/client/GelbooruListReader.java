package pt.yaku.client;

import java.io.IOException;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.Reader;
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
import com.google.gson.JsonParser;

@Provider
@Consumes("*/*")
@Produces("*/*")
public class GelbooruListReader<T> implements MessageBodyReader<List<T>> {

    private static final Gson GSON = new GsonBuilder()
            .setFieldNamingPolicy(FieldNamingPolicy.LOWER_CASE_WITH_UNDERSCORES)
            .create();

    private final String rootKey;
    private final Class<T> elementType;

    public GelbooruListReader(String rootKey, Class<T> elementType) {
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
        try (Reader reader = new InputStreamReader(entityStream, StandardCharsets.UTF_8)) {
            JsonArray items = JsonParser.parseReader(reader).getAsJsonObject().getAsJsonArray(rootKey);
            List<T> result = new ArrayList<>();
            if (items != null) {
                for (var element : items) {
                    result.add(GSON.fromJson(element, elementType));
                }
            }
            return result;
        }
    }
}
