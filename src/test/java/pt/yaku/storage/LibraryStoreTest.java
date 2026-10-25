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

    @Test
    public void testReadingRecordsAreStoredNewestFirstAndUpdatable() throws SQLException {
        try (LibraryStore store = LibraryStore.open(db)) {
            assertTrue(store.readingRecords().isEmpty());
            store.saveReading(new LibraryStore.ReadingRecord("https://e621.test#pool:7", "https://e621.test", 7,
                    "Comic", "series", 12, 48, false, 1000));
            store.saveReading(new LibraryStore.ReadingRecord("https://e621.test#pool:8", "https://e621.test", 8,
                    "Other", null, 3, 10, false, 2000));
            store.saveReading(new LibraryStore.ReadingRecord("https://e621.test#pool:7", "https://e621.test", 7,
                    "Comic", "series", 47, 48, true, 3000));

            List<LibraryStore.ReadingRecord> records = store.readingRecords();
            assertEquals(List.of(7L, 8L), records.stream().map(LibraryStore.ReadingRecord::poolId).toList());
            assertTrue(records.get(0).finished());
            assertEquals(47, records.get(0).page());
            assertNull(records.get(1).category());

            store.deleteReading("https://e621.test#pool:7");
            assertEquals(1, store.readingRecords().size());
        }
    }

    @Test
    public void testOlderReadingTableIsUpgradedInPlace() throws SQLException {
        try (Connection c = DriverManager.getConnection("jdbc:sqlite:" + db)) {
            c.createStatement().executeUpdate("CREATE TABLE reading_progress (pool_key TEXT PRIMARY KEY, "
                    + "page INTEGER NOT NULL, updated_at INTEGER NOT NULL)");
            c.createStatement().executeUpdate("INSERT INTO reading_progress VALUES ('old#pool:1', 5, 1)");
        }
        try (LibraryStore store = LibraryStore.open(db)) {
            assertTrue(store.readingRecords().isEmpty(), "rows without a pool name cannot be listed");
            store.saveReading(new LibraryStore.ReadingRecord("k", "https://x", 1, "n", null, 1, 2, false, 5));
            assertEquals(1, store.readingRecords().size());
        }
    }

    @Test
    public void testDownloadHistoryAndQueueSurviveReopen() throws Exception {
        try (LibraryStore store = LibraryStore.open(db)) {
            store.markDownloaded("a#1", 10);
            store.markDownloaded("a#2", 20);
            store.saveQueue(List.of(new LibraryStore.QueueEntry("a#3", "post", "{\"id\":3}"),
                    new LibraryStore.QueueEntry("a#pool:9", "pool", "{\"id\":9}")));
            store.saveQueue(List.of(new LibraryStore.QueueEntry("a#4", "post", "{\"id\":4}"),
                    new LibraryStore.QueueEntry("a#3", "post", "{\"id\":3}")));
        }
        try (LibraryStore store = LibraryStore.open(db)) {
            assertEquals(java.util.Set.of("a#1", "a#2"), store.downloadedKeys());
            assertEquals(List.of("a#4", "a#3"),
                    store.queueEntries().stream().map(LibraryStore.QueueEntry::key).toList());
            store.forgetDownloads();
            assertTrue(store.downloadedKeys().isEmpty());
        }
    }
}
