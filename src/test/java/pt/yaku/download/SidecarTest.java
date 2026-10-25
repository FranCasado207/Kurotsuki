package pt.yaku.download;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.nio.file.Files;
import java.nio.file.Path;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import pt.yaku.booru.Post;
import pt.yaku.booru.TagCategory;

public class SidecarTest {

    private static Post post() {
        Post post = new Post();
        post.setId(5);
        post.setTags("cat  dog");
        post.setSource("https://example.com/a\nb");
        post.setRating("e");
        post.addTagCategories("cat", TagCategory.CHARACTER);
        return post;
    }

    @Test
    public void testTxtListsTagsAndSource(@TempDir Path dir) throws Exception {
        Path media = dir.resolve("a.png");
        Files.writeString(media, "x");
        Sidecar.write(post(), media, SidecarFormat.TXT);
        assertEquals("cat\ndog\nsource:https://example.com/a b\n", Files.readString(dir.resolve("a.png.txt")));
    }

    @Test
    public void testJsonHasMetadata(@TempDir Path dir) throws Exception {
        Path media = dir.resolve("a.png");
        Sidecar.write(post(), media, SidecarFormat.JSON);
        JsonObject json = JsonParser.parseString(Files.readString(dir.resolve("a.png.json"))).getAsJsonObject();
        assertEquals(5, json.get("id").getAsLong());
        assertEquals("e", json.get("rating").getAsString());
        assertEquals(2, json.getAsJsonArray("tags").size());
        assertEquals("character", json.getAsJsonObject("tagCategories").get("cat").getAsString());
    }

    @Test
    public void testNoneWritesNothing(@TempDir Path dir) throws Exception {
        Sidecar.write(post(), dir.resolve("a.png"), SidecarFormat.NONE);
        try (var files = Files.list(dir)) {
            assertFalse(files.findAny().isPresent());
        }
        assertEquals(SidecarFormat.NONE, SidecarFormat.parse("bogus"));
        assertEquals(SidecarFormat.JSON, SidecarFormat.parse("Json"));
        assertTrue(SidecarFormat.TXT.suffix().startsWith("."));
    }
}
