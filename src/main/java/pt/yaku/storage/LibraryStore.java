package pt.yaku.storage;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.TimeUnit;
import java.util.function.Consumer;
import java.util.logging.Level;
import java.util.logging.Logger;
import java.util.regex.Pattern;
import java.sql.Connection;
import java.sql.DriverManager;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.sql.Statement;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collection;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

import pt.yaku.booru.Post;
import pt.yaku.booru.TagCategory;
import pt.yaku.context.ApplicationContext;
import pt.yaku.user.config.AppPaths;
import pt.yaku.user.config.ConfigManager;
import pt.yaku.user.config.FolderData;
import pt.yaku.user.config.UserConfiguration;

public final class LibraryStore implements AutoCloseable {

    private static final String DB_FILE = "kurotsuki.db";
    private static final String MIGRATED_FLAG = "migrated_from_json";
    private static final String SECRETS_STRIPPED_FLAG = "hydrus_keys_stripped";

    private static final Logger LOGGER = Logger.getLogger(LibraryStore.class.getName());
    private static final int BATCH = 400;
    private static final Pattern SECRET_PARAM = Pattern.compile(
            "\\?Hydrus-Client-API-Access-Key=[^&#]*&", Pattern.CASE_INSENSITIVE);
    private static final Pattern SECRET_AFTER_QUESTION = Pattern.compile(
            "[?&]Hydrus-Client-API-Access-Key=[^&#]*", Pattern.CASE_INSENSITIVE);

    private final Connection connection;
    private final ExecutorService writer = Executors.newSingleThreadExecutor(runnable -> {
        Thread thread = new Thread(runnable, "library-writer");
        thread.setDaemon(true);
        return thread;
    });

    private LibraryStore(Connection connection) {
        this.connection = connection;
    }

    public static LibraryStore open(ApplicationContext context) throws SQLException {
        Path folder = AppPaths.get().data();
        try {
            Files.createDirectories(folder);
        } catch (IOException e) {
            throw new SQLException("Could not create " + folder, e);
        }
        ConfigManager.restrictToOwner(folder);
        Path file = folder.resolve(DB_FILE);
        LibraryStore store = open(file);
        ConfigManager.restrictToOwner(file);
        store.migrateFromJson(context.getUserConfig(), context::saveUserConfig);
        return store;
    }

    public static LibraryStore open(Path file) throws SQLException {
        Connection connection = DriverManager.getConnection("jdbc:sqlite:" + file);
        LibraryStore store = new LibraryStore(connection);
        store.createSchema();
        store.stripSecrets();
        Runtime.getRuntime().addShutdownHook(new Thread(() -> {
            try {
                store.close();
            } catch (SQLException e) {
                LOGGER.log(Level.WARNING, "Could not close the library database", e);
            }
        }, "library-store-close"));
        return store;
    }

    @FunctionalInterface
    public interface SqlTask {
        void run() throws SQLException;
    }

    /**
     * Runs a write on the store's single writer thread so the UI never waits on disk. Writes are applied in
     * submission order; {@code onError} is called on the writer thread if the write fails.
     */
    public void submit(SqlTask task, Consumer<SQLException> onError) {
        try {
            writer.execute(() -> {
                try {
                    task.run();
                } catch (SQLException e) {
                    onError.accept(e);
                }
            });
        } catch (java.util.concurrent.RejectedExecutionException e) {
            onError.accept(new SQLException("Library is closed", e));
        }
    }

    private void createSchema() throws SQLException {
        try (Statement statement = connection.createStatement()) {
            statement.execute("PRAGMA foreign_keys = ON");
            statement.execute("""
                    CREATE TABLE IF NOT EXISTS meta (
                        name TEXT PRIMARY KEY,
                        value TEXT
                    )""");
            statement.execute("""
                    CREATE TABLE IF NOT EXISTS posts (
                        post_key TEXT PRIMARY KEY,
                        booru_base_url TEXT,
                        id INTEGER NOT NULL,
                        score INTEGER,
                        width INTEGER,
                        height INTEGER,
                        image TEXT,
                        rating TEXT,
                        source TEXT,
                        tags TEXT,
                        preview_url TEXT,
                        file_url TEXT,
                        sample_url TEXT
                    )""");
            statement.execute("""
                    CREATE TABLE IF NOT EXISTS post_tags (
                        post_key TEXT NOT NULL REFERENCES posts(post_key) ON DELETE CASCADE,
                        tag TEXT NOT NULL,
                        category TEXT NOT NULL,
                        PRIMARY KEY (post_key, tag)
                    )""");
            statement.execute("""
                    CREATE TABLE IF NOT EXISTS tag_categories (
                        booru_base_url TEXT NOT NULL,
                        tag TEXT NOT NULL,
                        category TEXT NOT NULL,
                        PRIMARY KEY (booru_base_url, tag)
                    )""");
            statement.execute("""
                    CREATE TABLE IF NOT EXISTS favorite_tags (
                        tag TEXT PRIMARY KEY
                    )""");
            statement.execute("""
                    CREATE TABLE IF NOT EXISTS favorite_posts (
                        post_key TEXT PRIMARY KEY REFERENCES posts(post_key) ON DELETE CASCADE
                    )""");
            statement.execute("""
                    CREATE TABLE IF NOT EXISTS tag_groups (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        name TEXT NOT NULL UNIQUE
                    )""");
            statement.execute("""
                    CREATE TABLE IF NOT EXISTS tag_group_tags (
                        group_id INTEGER NOT NULL REFERENCES tag_groups(id) ON DELETE CASCADE,
                        tag TEXT NOT NULL REFERENCES favorite_tags(tag) ON DELETE CASCADE,
                        PRIMARY KEY (group_id, tag)
                    )""");
            statement.execute("""
                    CREATE TABLE IF NOT EXISTS folders (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        name TEXT NOT NULL UNIQUE
                    )""");
            statement.execute("""
                    CREATE TABLE IF NOT EXISTS folder_posts (
                        folder_id INTEGER NOT NULL REFERENCES folders(id) ON DELETE CASCADE,
                        post_key TEXT NOT NULL REFERENCES posts(post_key) ON DELETE CASCADE,
                        PRIMARY KEY (folder_id, post_key)
                    )""");
            statement.execute("CREATE INDEX IF NOT EXISTS idx_folder_posts_post ON folder_posts(post_key)");
        }
    }


    public synchronized Map<String, TagCategory> tagCategories(String booruBaseUrl, Collection<String> tags)
            throws SQLException {
        Map<String, TagCategory> categories = new LinkedHashMap<>();
        for (List<String> chunk : chunks(new ArrayList<>(tags))) {
            try (PreparedStatement statement = connection.prepareStatement(
                    "SELECT tag, category FROM tag_categories WHERE booru_base_url = ? AND tag IN ("
                            + placeholders(chunk.size()) + ")")) {
                statement.setString(1, booruBaseUrl);
                for (int i = 0; i < chunk.size(); i++) {
                    statement.setString(i + 2, chunk.get(i));
                }
                try (ResultSet row = statement.executeQuery()) {
                    while (row.next()) {
                        categories.put(row.getString(1), TagCategory.valueOf(row.getString(2)));
                    }
                }
            }
        }
        return categories;
    }

    public synchronized void saveTagCategories(String booruBaseUrl, Map<String, TagCategory> categories)
            throws SQLException {
        inTransaction(() -> {
            for (Map.Entry<String, TagCategory> entry : categories.entrySet()) {
                execute("INSERT OR REPLACE INTO tag_categories (booru_base_url, tag, category) VALUES (?, ?, ?)",
                        booruBaseUrl, entry.getKey(), entry.getValue().name());
            }
        });
    }


    public synchronized List<String> favoriteTags() throws SQLException {
        List<String> tags = new ArrayList<>();
        try (PreparedStatement statement = connection.prepareStatement(
                "SELECT tag FROM favorite_tags ORDER BY rowid");
             ResultSet rows = statement.executeQuery()) {
            while (rows.next()) {
                tags.add(rows.getString(1));
            }
        }
        return tags;
    }

    public synchronized void addFavoriteTag(String tag) throws SQLException {
        execute("INSERT OR IGNORE INTO favorite_tags (tag) VALUES (?)", tag);
    }

    public synchronized void removeFavoriteTag(String tag) throws SQLException {
        execute("DELETE FROM favorite_tags WHERE tag = ?", tag);
    }


    /** Group name to its tags, in creation order. Groups without tags are included. */
    public synchronized Map<String, List<String>> tagGroups() throws SQLException {
        Map<String, List<String>> groups = new LinkedHashMap<>();
        try (PreparedStatement statement = connection.prepareStatement("""
                SELECT g.name, t.tag
                FROM tag_groups g
                LEFT JOIN tag_group_tags t ON t.group_id = g.id
                ORDER BY g.id, t.rowid""");
             ResultSet rows = statement.executeQuery()) {
            while (rows.next()) {
                List<String> tags = groups.computeIfAbsent(rows.getString(1), name -> new ArrayList<>());
                String tag = rows.getString(2);
                if (tag != null) {
                    tags.add(tag);
                }
            }
        }
        return groups;
    }

    public synchronized void createTagGroup(String name) throws SQLException {
        execute("INSERT OR IGNORE INTO tag_groups (name) VALUES (?)", name);
    }

    public synchronized void renameTagGroup(String name, String newName) throws SQLException {
        execute("UPDATE tag_groups SET name = ? WHERE name = ?", newName, name);
    }

    public synchronized void deleteTagGroup(String name) throws SQLException {
        execute("DELETE FROM tag_groups WHERE name = ?", name);
    }

    /** The tag must already be a favorite (enforced by a foreign key). */
    public synchronized void addTagToGroup(String group, String tag) throws SQLException {
        execute("INSERT OR IGNORE INTO tag_group_tags (group_id, tag) "
                + "VALUES ((SELECT id FROM tag_groups WHERE name = ?), ?)", group, tag);
    }

    public synchronized void removeTagFromGroup(String group, String tag) throws SQLException {
        execute("DELETE FROM tag_group_tags WHERE group_id = (SELECT id FROM tag_groups WHERE name = ?) "
                + "AND tag = ?", group, tag);
    }

    public synchronized List<Post> favoritePosts() throws SQLException {
        return postsFor(keysOf("SELECT post_key FROM favorite_posts ORDER BY rowid"));
    }

    public synchronized void setFavoritePost(Post post, boolean favorite) throws SQLException {
        inTransaction(() -> {
            if (favorite) {
                savePost(post);
                execute("INSERT OR IGNORE INTO favorite_posts (post_key) VALUES (?)", post.key());
            } else {
                execute("DELETE FROM favorite_posts WHERE post_key = ?", post.key());
                pruneUnusedPosts();
            }
        });
    }


    public synchronized Map<String, List<Post>> folders() throws SQLException {
        Map<String, List<String>> keysByFolder = new LinkedHashMap<>();
        try (PreparedStatement statement = connection.prepareStatement("""
                SELECT f.name, fp.post_key
                FROM folders f
                LEFT JOIN folder_posts fp ON fp.folder_id = f.id
                ORDER BY f.id, fp.rowid""");
             ResultSet rows = statement.executeQuery()) {
            while (rows.next()) {
                List<String> keys = keysByFolder.computeIfAbsent(rows.getString(1), name -> new ArrayList<>());
                String key = rows.getString(2);
                if (key != null) {
                    keys.add(key);
                }
            }
        }
        Set<String> allKeys = new LinkedHashSet<>();
        keysByFolder.values().forEach(allKeys::addAll);
        Map<String, Post> postsByKey = new HashMap<>();
        for (Post post : postsFor(new ArrayList<>(allKeys))) {
            postsByKey.put(post.key(), post);
        }
        Map<String, List<Post>> folders = new LinkedHashMap<>();
        for (Map.Entry<String, List<String>> folder : keysByFolder.entrySet()) {
            List<Post> posts = new ArrayList<>();
            for (String key : folder.getValue()) {
                Post post = postsByKey.get(key);
                if (post != null) {
                    posts.add(post);
                }
            }
            folders.put(folder.getKey(), posts);
        }
        return folders;
    }

    public synchronized void createFolder(String name) throws SQLException {
        execute("INSERT OR IGNORE INTO folders (name) VALUES (?)", name);
    }

    public synchronized void deleteFolder(String name) throws SQLException {
        inTransaction(() -> {
            execute("DELETE FROM folders WHERE name = ?", name);
            pruneUnusedPosts();
        });
    }

    public synchronized void addToFolder(String folder, Post post) throws SQLException {
        inTransaction(() -> {
            savePost(post);
            execute("INSERT OR IGNORE INTO folder_posts (folder_id, post_key) "
                    + "VALUES ((SELECT id FROM folders WHERE name = ?), ?)", folder, post.key());
        });
    }

    public synchronized void removeFromFolder(String folder, Post post) throws SQLException {
        inTransaction(() -> {
            execute("DELETE FROM folder_posts WHERE folder_id = (SELECT id FROM folders WHERE name = ?) "
                    + "AND post_key = ?", folder, post.key());
            pruneUnusedPosts();
        });
    }


    private void savePost(Post post) throws SQLException {
        try (PreparedStatement statement = connection.prepareStatement("""
                INSERT INTO posts (post_key, booru_base_url, id, score, width, height, image, rating,
                                   source, tags, preview_url, file_url, sample_url)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                ON CONFLICT(post_key) DO UPDATE SET
                    score = excluded.score, width = excluded.width, height = excluded.height,
                    image = excluded.image, rating = excluded.rating, source = excluded.source,
                    tags = excluded.tags, preview_url = excluded.preview_url,
                    file_url = excluded.file_url, sample_url = excluded.sample_url""")) {
            statement.setString(1, post.key());
            statement.setString(2, post.getBooruBaseUrl());
            statement.setLong(3, post.getId());
            statement.setInt(4, post.getScore());
            statement.setInt(5, post.getWidth());
            statement.setInt(6, post.getHeight());
            statement.setString(7, post.getImage());
            statement.setString(8, post.getRating());
            statement.setString(9, post.getSource());
            statement.setString(10, post.getTags());
            statement.setString(11, withoutSecrets(post.getPreviewUrl()));
            statement.setString(12, withoutSecrets(post.getFileUrl()));
            statement.setString(13, withoutSecrets(post.getSampleUrl()));
            statement.executeUpdate();
        }
        execute("DELETE FROM post_tags WHERE post_key = ?", post.key());
        try (PreparedStatement statement = connection.prepareStatement(
                "INSERT OR IGNORE INTO post_tags (post_key, tag, category) VALUES (?, ?, ?)")) {
            for (String tag : splitTags(post.getTags())) {
                statement.setString(1, post.key());
                statement.setString(2, tag);
                statement.setString(3, post.categoryOf(tag).name());
                statement.addBatch();
            }
            statement.executeBatch();
        }
    }

    private List<String> keysOf(String sql) throws SQLException {
        List<String> keys = new ArrayList<>();
        try (PreparedStatement statement = connection.prepareStatement(sql);
             ResultSet rows = statement.executeQuery()) {
            while (rows.next()) {
                keys.add(rows.getString(1));
            }
        }
        return keys;
    }

    /** Loads posts in two queries per chunk of keys instead of two per post; keeps the order of {@code keys}. */
    private List<Post> postsFor(List<String> keys) throws SQLException {
        Map<String, Post> byKey = new HashMap<>();
        for (List<String> chunk : chunks(keys)) {
            String in = placeholders(chunk.size());
            try (PreparedStatement statement = connection.prepareStatement(
                    "SELECT post_key, booru_base_url, id, score, width, height, image, rating, source, tags, "
                            + "preview_url, file_url, sample_url FROM posts WHERE post_key IN (" + in + ")")) {
                bind(statement, chunk);
                try (ResultSet row = statement.executeQuery()) {
                    while (row.next()) {
                        Post post = new Post();
                        post.setBooruBaseUrl(row.getString(2));
                        post.setId(row.getLong(3));
                        post.setScore(row.getInt(4));
                        post.setWidth(row.getInt(5));
                        post.setHeight(row.getInt(6));
                        post.setImage(row.getString(7));
                        post.setRating(row.getString(8));
                        post.setSource(row.getString(9));
                        post.setTags(row.getString(10));
                        post.setPreviewUrl(row.getString(11));
                        post.setFileUrl(row.getString(12));
                        post.setSampleUrl(row.getString(13));
                        byKey.put(row.getString(1), post);
                    }
                }
            }
            try (PreparedStatement statement = connection.prepareStatement(
                    "SELECT post_key, tag, category FROM post_tags WHERE post_key IN (" + in + ")")) {
                bind(statement, chunk);
                try (ResultSet rows = statement.executeQuery()) {
                    while (rows.next()) {
                        Post post = byKey.get(rows.getString(1));
                        if (post != null) {
                            post.getTagCategories().put(rows.getString(2), TagCategory.valueOf(rows.getString(3)));
                        }
                    }
                }
            }
        }
        List<Post> posts = new ArrayList<>();
        for (String key : keys) {
            Post post = byKey.get(key);
            if (post != null) {
                posts.add(post);
            }
        }
        return posts;
    }

    private void pruneUnusedPosts() throws SQLException {
        execute("""
                DELETE FROM posts
                WHERE NOT EXISTS (SELECT 1 FROM favorite_posts f WHERE f.post_key = posts.post_key)
                  AND NOT EXISTS (SELECT 1 FROM folder_posts p WHERE p.post_key = posts.post_key)""");
    }

    /** Older versions stored the Hydrus access key inside file/preview URLs; remove it from existing rows. */
    private void stripSecrets() throws SQLException {
        if (metaValue(SECRETS_STRIPPED_FLAG) != null) {
            return;
        }
        inTransaction(() -> {
            List<String[]> rows = new ArrayList<>();
            try (PreparedStatement statement = connection.prepareStatement(
                    "SELECT post_key, preview_url, file_url, sample_url FROM posts "
                            + "WHERE preview_url LIKE '%Access-Key%' OR file_url LIKE '%Access-Key%' "
                            + "OR sample_url LIKE '%Access-Key%'");
                 ResultSet result = statement.executeQuery()) {
                while (result.next()) {
                    rows.add(new String[] {result.getString(1), result.getString(2), result.getString(3),
                            result.getString(4)});
                }
            }
            for (String[] row : rows) {
                execute("UPDATE posts SET preview_url = ?, file_url = ?, sample_url = ? WHERE post_key = ?",
                        withoutSecrets(row[1]), withoutSecrets(row[2]), withoutSecrets(row[3]), row[0]);
            }
            execute("INSERT OR REPLACE INTO meta (name, value) VALUES (?, ?)", SECRETS_STRIPPED_FLAG, "1");
        });
    }

    static String withoutSecrets(String url) {
        if (url == null) {
            return null;
        }
        return SECRET_AFTER_QUESTION.matcher(SECRET_PARAM.matcher(url).replaceAll("?")).replaceAll("")
                .replaceAll("[?&]$", "");
    }

    @FunctionalInterface
    public interface ConfigSaver {
        void save() throws IOException;
    }

    private void inTransaction(SqlTask work) throws SQLException {
        if (!connection.getAutoCommit()) {
            work.run();
            return;
        }
        connection.setAutoCommit(false);
        try {
            work.run();
            connection.commit();
        } catch (SQLException | RuntimeException e) {
            connection.rollback();
            throw e;
        } finally {
            connection.setAutoCommit(true);
        }
    }

    /**
     * One-time import of favourites and folders that used to live in config.json. The flag is committed together
     * with the data, so a failed import is retried; if clearing config.json fails afterwards the stale copy is
     * harmless (the flag prevents a second import) and is removed on the next successful config save.
     */
    private synchronized void migrateFromJson(UserConfiguration config, ConfigSaver saver) throws SQLException {
        if (metaValue(MIGRATED_FLAG) != null) {
            return;
        }
        inTransaction(() -> {
            if (config.getFavoriteTags() != null) {
                for (String tag : config.getFavoriteTags()) {
                    addFavoriteTag(tag);
                }
            }
            if (config.getFavoritePosts() != null) {
                for (Post post : config.getFavoritePosts()) {
                    setFavoritePost(post, true);
                }
            }
            if (config.getPostFolders() != null) {
                for (FolderData folder : config.getPostFolders()) {
                    createFolder(folder.getName());
                    if (folder.getPosts() != null) {
                        for (Post post : folder.getPosts()) {
                            addToFolder(folder.getName(), post);
                        }
                    }
                }
            }
            execute("INSERT INTO meta (name, value) VALUES (?, ?)", MIGRATED_FLAG, "1");
        });
        config.setFavoriteTags(null);
        config.setFavoritePosts(null);
        config.setPostFolders(null);
        try {
            saver.save();
        } catch (IOException e) {
            LOGGER.log(Level.WARNING, "Imported favourites and folders, but could not remove them from config.json",
                    e);
        }
    }

    private String metaValue(String name) throws SQLException {
        try (PreparedStatement statement = connection.prepareStatement("SELECT value FROM meta WHERE name = ?")) {
            statement.setString(1, name);
            try (ResultSet rows = statement.executeQuery()) {
                return rows.next() ? rows.getString(1) : null;
            }
        }
    }


    private void execute(String sql, Object... params) throws SQLException {
        try (PreparedStatement statement = connection.prepareStatement(sql)) {
            for (int i = 0; i < params.length; i++) {
                statement.setObject(i + 1, params[i]);
            }
            statement.executeUpdate();
        }
    }

    private static List<String> splitTags(String raw) {
        if (raw == null || raw.isBlank()) {
            return List.of();
        }
        return Arrays.stream(raw.trim().split("\\s+")).filter(tag -> !tag.isBlank()).toList();
    }

    private static List<List<String>> chunks(List<String> values) {
        List<List<String>> chunks = new ArrayList<>();
        for (int i = 0; i < values.size(); i += BATCH) {
            chunks.add(values.subList(i, Math.min(values.size(), i + BATCH)));
        }
        return chunks;
    }

    private static String placeholders(int count) {
        return String.join(",", java.util.Collections.nCopies(count, "?"));
    }

    private static void bind(PreparedStatement statement, List<String> values) throws SQLException {
        for (int i = 0; i < values.size(); i++) {
            statement.setString(i + 1, values.get(i));
        }
    }

    /** Waits for queued writes, then closes the connection. Safe to call more than once. */
    @Override
    public void close() throws SQLException {
        writer.shutdown();
        try {
            if (!writer.awaitTermination(5, TimeUnit.SECONDS)) {
                LOGGER.warning("Library writes were still pending when closing the database");
            }
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
        }
        synchronized (this) {
            if (!connection.isClosed()) {
                connection.close();
            }
        }
    }
}
