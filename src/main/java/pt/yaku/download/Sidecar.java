package pt.yaku.download;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;

import pt.yaku.booru.Post;

public final class Sidecar {

    private static final Gson GSON = new GsonBuilder().setPrettyPrinting().disableHtmlEscaping().create();

    private Sidecar() {
    }

    public static Path pathFor(Path media, SidecarFormat format) {
        return media.resolveSibling(media.getFileName() + format.suffix());
    }

    /** Writes the sidecar for a saved file; does nothing for {@link SidecarFormat#NONE}. */
    public static void write(Post post, Path media, SidecarFormat format) throws IOException {
        if (format == SidecarFormat.NONE) {
            return;
        }
        String content = format == SidecarFormat.TXT ? text(post) : json(post);
        Files.writeString(pathFor(media, format), content, StandardCharsets.UTF_8);
    }

    static List<String> tagsOf(Post post) {
        List<String> tags = new ArrayList<>();
        if (post.getTags() != null) {
            for (String tag : post.getTags().trim().split("\\s+")) {
                if (!tag.isEmpty()) {
                    tags.add(tag);
                }
            }
        }
        return tags;
    }

    static String text(Post post) {
        StringBuilder out = new StringBuilder();
        for (String tag : tagsOf(post)) {
            out.append(tag).append('\n');
        }
        if (post.getSource() != null && !post.getSource().isBlank()) {
            out.append("source:").append(post.getSource().strip().replaceAll("[\\r\\n]+", " ")).append('\n');
        }
        return out.toString();
    }

    static String json(Post post) {
        Map<String, Object> data = new LinkedHashMap<>();
        data.put("booru", post.getBooruBaseUrl());
        data.put("id", post.getId());
        data.put("url", post.getFileUrl());
        data.put("source", post.getSource());
        data.put("rating", post.getRating());
        data.put("score", post.getScore());
        data.put("width", post.getWidth());
        data.put("height", post.getHeight());
        data.put("tags", tagsOf(post));
        Map<String, String> categories = new LinkedHashMap<>();
        if (post.getTagCategories() != null) {
            post.getTagCategories().forEach((tag, category) -> categories.put(tag, category.name().toLowerCase()));
        }
        data.put("tagCategories", categories);
        return GSON.toJson(data) + "\n";
    }
}
