package pt.yaku.storage;

import java.nio.file.Files;
import java.nio.file.Path;
import java.sql.Connection;
import java.sql.DriverManager;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.util.List;
import java.util.Map;

import static org.junit.jupiter.api.Assertions.*;

import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import pt.yaku.booru.Post;

public class LibraryStoreTest {

    private Path dir;
    private Path db;

    @BeforeEach
    void setUp() throws Exception {
        dir = Files.createTempDirectory("kurotsuki-db");
        db = dir.resolve("test.db");
    }

    @AfterEach
    void tearDown() throws Exception {
        try (var files = Files.list(dir)) {
            for (Path file : files.toList()) {
                Files.deleteIfExists(file);
            }
        }
        Files.deleteIfExists(dir);
    }

    private static Post post(long id, String fileUrl) {
        Post post = new Post();
        post.setId(id);
        post.setBooruBaseUrl("https://booru.test");
        post.setTags("a b");
        post.setFileUrl(fileUrl);
        post.setPreviewUrl(fileUrl);
        return post;
    }

    @Test
    public void testWithoutSecrets() {
        String key = "Hydrus-Client-API-Access-Key";
        assertEquals("http://h/get_files/file?file_id=1",
                LibraryStore.withoutSecrets("http://h/get_files/file?file_id=1&" + key + "=abc"));
        assertEquals("http://h/get_files/file?file_id=1",
                LibraryStore.withoutSecrets("http://h/get_files/file?" + key + "=abc&file_id=1"));
        assertEquals("http://h/x", LibraryStore.withoutSecrets("http://h/x?" + key + "=abc"));
        assertEquals("http://h/x?a=1", LibraryStore.withoutSecrets("http://h/x?a=1"));
        assertNull(LibraryStore.withoutSecrets(null));
    }

    @Test
    public void testFavoritesAndFoldersRoundTripWithoutSecrets() throws SQLException {
        try (LibraryStore store = LibraryStore.open(db)) {
            Post secret = post(1, "http://h/f?file_id=1&Hydrus-Client-API-Access-Key=abc");
            store.setFavoritePost(secret, true);
            store.createFolder("Wallpapers");
            store.addToFolder("Wallpapers", post(2, "http://h/f?file_id=2"));

            List<Post> favorites = store.favoritePosts();
            assertEquals(1, favorites.size());
            assertEquals("http://h/f?file_id=1", favorites.get(0).getFileUrl());
            assertEquals(2, favorites.get(0).getTagCategories().size());

            Map<String, List<Post>> folders = store.folders();
            assertEquals(1, folders.get("Wallpapers").size());
            assertEquals(2L, folders.get("Wallpapers").get(0).getId());
        }
    }

    @Test
    public void testUnusedPostsArePruned() throws SQLException {
        try (LibraryStore store = LibraryStore.open(db)) {
            Post post = post(1, "http://h/f");
            store.setFavoritePost(post, true);
            store.createFolder("f");
            store.addToFolder("f", post);
            store.setFavoritePost(post, false);
            assertEquals(1, store.folders().get("f").size());
            store.removeFromFolder("f", post);
            assertEquals(0, store.folders().get("f").size());
            try (Connection c = DriverManager.getConnection("jdbc:sqlite:" + db);
                 ResultSet rows = c.createStatement().executeQuery("SELECT COUNT(*) FROM posts")) {
                rows.next();
                assertEquals(0, rows.getInt(1));
            }
        }
    }

    @Test
    public void testExistingKeysAreStrippedOnOpen() throws SQLException {
        try (LibraryStore store = LibraryStore.open(db)) {
            store.setFavoritePost(post(1, "http://h/f"), true);
        }
        try (Connection c = DriverManager.getConnection("jdbc:sqlite:" + db)) {
            c.createStatement().executeUpdate("UPDATE posts SET file_url = "
                    + "'http://h/f?file_id=1&Hydrus-Client-API-Access-Key=abc'");
            c.createStatement().executeUpdate("DELETE FROM meta WHERE name = 'hydrus_keys_stripped'");
        }
        try (LibraryStore store = LibraryStore.open(db)) {
            assertEquals("http://h/f?file_id=1", store.favoritePosts().get(0).getFileUrl());
        }
    }

    @Test
    public void testWritesAreAppliedInOrderOnTheWriterThread() throws Exception {
        try (LibraryStore store = LibraryStore.open(db)) {
            store.submit(() -> store.addFavoriteTag("x"), e -> fail());
            store.submit(() -> store.removeFavoriteTag("x"), e -> fail());
            store.submit(() -> store.addFavoriteTag("y"), e -> fail());
            store.close();
        }
        try (LibraryStore store = LibraryStore.open(db)) {
            assertEquals(List.of("y"), store.favoriteTags());
        }
    }

    @Test
    public void testTagGroupsKeepOrderAndFollowFavorites() throws SQLException {
        try (LibraryStore store = LibraryStore.open(db)) {
            store.addFavoriteTag("cat");
            store.addFavoriteTag("dog");
            store.createTagGroup("pets");
            store.createTagGroup("empty");
            store.addTagToGroup("pets", "cat");
            store.addTagToGroup("pets", "dog");
            assertEquals(List.of("cat", "dog"), store.tagGroups().get("pets"));
            assertEquals(List.of(), store.tagGroups().get("empty"));

            store.renameTagGroup("pets", "animals");
            assertEquals(List.of("cat", "dog"), store.tagGroups().get("animals"));

            store.removeFavoriteTag("dog");
            assertEquals(List.of("cat"), store.tagGroups().get("animals"));

            store.deleteTagGroup("animals");
            assertFalse(store.tagGroups().containsKey("animals"));
            assertEquals(List.of("cat"), store.favoriteTags());
        }
    }

    @Test
    public void testOnlyFavoriteTagsCanJoinAGroup() throws SQLException {
        try (LibraryStore store = LibraryStore.open(db)) {
            store.createTagGroup("g");
            assertThrows(SQLException.class, () -> store.addTagToGroup("g", "not-a-favorite"));
        }
    }
}
