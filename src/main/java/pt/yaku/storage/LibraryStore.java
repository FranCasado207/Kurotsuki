package pt.yaku.storage;

import java.io.IOException;
import java.nio.file.Path;
import java.sql.Connection;
import java.sql.DriverManager;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.sql.Statement;
import java.util.ArrayList;
import java.util.Collection;
import java.util.Arrays;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

import pt.yaku.booru.Post;
import pt.yaku.booru.TagCategory;
import pt.yaku.context.ApplicationContext;
import pt.yaku.user.config.ConfigManager;
import pt.yaku.user.config.FolderData;
import pt.yaku.user.config.UserConfiguration;

public final class LibraryStore implements AutoCloseable {

    private static final String DB_FILE = "kurotsuki.db";
    private static final String MIGRATED_FLAG = "migrated_from_json";

    private final Connection connection;

    private LibraryStore(Connection connection) {
        this.connection = connection;
    }

    public static LibraryStore open(ApplicationContext context) throws SQLException {
        Path file = ConfigManager.getFolderPath().resolve(DB_FILE);
        Connection connection = DriverManager.getConnection("jdbc:sqlite:" + file);
        LibraryStore store = new LibraryStore(connection);
        store.createSchema();
        store.migrateFromJson(context);
        return store;
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
        }
    }


    public Map<String, TagCategory> tagCategories(String booruBaseUrl, Collection<String> tags) throws SQLException {
        Map<String, TagCategory> categories = new LinkedHashMap<>();
        for (String tag : tags) {
            try (PreparedStatement statement = connection.prepareStatement(
                    "SELECT category FROM tag_categories WHERE booru_base_url = ? AND tag = ?")) {
                statement.setString(1, booruBaseUrl);
                statement.setString(2, tag);
                try (ResultSet row = statement.executeQuery()) {
                    if (row.next()) {
                        categories.put(tag, TagCategory.valueOf(row.getString(1)));
                    }
                }
            }
        }
        return categories;
    }

    public void saveTagCategories(String booruBaseUrl, Map<String, TagCategory> categories) throws SQLException {
        for (Map.Entry<String, TagCategory> entry : categories.entrySet()) {
            execute("INSERT OR REPLACE INTO tag_categories (booru_base_url, tag, category) VALUES (?, ?, ?)",
                    booruBaseUrl, entry.getKey(), entry.getValue().name());
        }
    }


    public List<String> favoriteTags() throws SQLException {
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

    public void addFavoriteTag(String tag) throws SQLException {
        execute("INSERT OR IGNORE INTO favorite_tags (tag) VALUES (?)", tag);
    }

    public void removeFavoriteTag(String tag) throws SQLException {
        execute("DELETE FROM favorite_tags WHERE tag = ?", tag);
    }


    public List<Post> favoritePosts() throws SQLException {
        return postsFor("SELECT post_key FROM favorite_posts ORDER BY rowid");
    }

    public void setFavoritePost(Post post, boolean favorite) throws SQLException {
        if (favorite) {
            savePost(post);
            execute("INSERT OR IGNORE INTO favorite_posts (post_key) VALUES (?)", post.key());
        } else {
            execute("DELETE FROM favorite_posts WHERE post_key = ?", post.key());
            pruneUnusedPosts();
        }
    }


    public Map<String, List<Post>> folders() throws SQLException {
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
        Map<String, List<Post>> folders = new LinkedHashMap<>();
        for (Map.Entry<String, List<String>> folder : keysByFolder.entrySet()) {
            folders.put(folder.getKey(), postsFor(folder.getValue()));
        }
        return folders;
    }

    public void createFolder(String name) throws SQLException {
        execute("INSERT OR IGNORE INTO folders (name) VALUES (?)", name);
    }

    public void deleteFolder(String name) throws SQLException {
        execute("DELETE FROM folders WHERE name = ?", name);
        pruneUnusedPosts();
    }

    public void addToFolder(String folder, Post post) throws SQLException {
        savePost(post);
        execute("INSERT OR IGNORE INTO folder_posts (folder_id, post_key) "
                + "VALUES ((SELECT id FROM folders WHERE name = ?), ?)", folder, post.key());
    }

    public void removeFromFolder(String folder, Post post) throws SQLException {
        execute("DELETE FROM folder_posts WHERE folder_id = (SELECT id FROM folders WHERE name = ?) "
                + "AND post_key = ?", folder, post.key());
        pruneUnusedPosts();
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
            statement.setString(11, post.getPreviewUrl());
            statement.setString(12, post.getFileUrl());
            statement.setString(13, post.getSampleUrl());
            statement.executeUpdate();
        }
        execute("DELETE FROM post_tags WHERE post_key = ?", post.key());
        for (String tag : splitTags(post.getTags())) {
            execute("INSERT OR IGNORE INTO post_tags (post_key, tag, category) VALUES (?, ?, ?)",
                    post.key(), tag, post.categoryOf(tag).name());
        }
    }

    private List<Post> postsFor(String sql) throws SQLException {
        List<String> keys = new ArrayList<>();
        try (PreparedStatement statement = connection.prepareStatement(sql);
             ResultSet rows = statement.executeQuery()) {
            while (rows.next()) {
                keys.add(rows.getString(1));
            }
        }
        return postsFor(keys);
    }

    private List<Post> postsFor(List<String> keys) throws SQLException {
        List<Post> posts = new ArrayList<>();
        for (String key : keys) {
            Post post = loadPost(key);
            if (post != null) {
                posts.add(post);
            }
        }
        return posts;
    }

    private Post loadPost(String key) throws SQLException {
        Post post = null;
        try (PreparedStatement statement = connection.prepareStatement("""
                SELECT booru_base_url, id, score, width, height, image, rating, source, tags,
                       preview_url, file_url, sample_url
                FROM posts WHERE post_key = ?""")) {
            statement.setString(1, key);
            try (ResultSet row = statement.executeQuery()) {
                if (row.next()) {
                    post = new Post();
                    post.setBooruBaseUrl(row.getString(1));
                    post.setId(row.getLong(2));
                    post.setScore(row.getInt(3));
                    post.setWidth(row.getInt(4));
                    post.setHeight(row.getInt(5));
                    post.setImage(row.getString(6));
                    post.setRating(row.getString(7));
                    post.setSource(row.getString(8));
                    post.setTags(row.getString(9));
                    post.setPreviewUrl(row.getString(10));
                    post.setFileUrl(row.getString(11));
                    post.setSampleUrl(row.getString(12));
                }
            }
        }
        if (post == null) {
            return null;
        }
        try (PreparedStatement statement = connection.prepareStatement(
                "SELECT tag, category FROM post_tags WHERE post_key = ?")) {
            statement.setString(1, key);
            try (ResultSet rows = statement.executeQuery()) {
                while (rows.next()) {
                    post.getTagCategories().put(rows.getString(1), TagCategory.valueOf(rows.getString(2)));
                }
            }
        }
        return post;
    }

    private void pruneUnusedPosts() throws SQLException {
        execute("""
                DELETE FROM posts WHERE post_key NOT IN (SELECT post_key FROM favorite_posts)
                AND post_key NOT IN (SELECT post_key FROM folder_posts)""");
    }


    private void migrateFromJson(ApplicationContext context) throws SQLException {
        if (metaValue(MIGRATED_FLAG) != null) {
            return;
        }
        UserConfiguration config = context.getUserConfig();
        connection.setAutoCommit(false);
        try {
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
            connection.commit();
        } catch (SQLException e) {
            connection.rollback();
            throw e;
        } finally {
            connection.setAutoCommit(true);
        }
        config.setFavoriteTags(null);
        config.setFavoritePosts(null);
        config.setPostFolders(null);
        try {
            context.saveUserConfig();
        } catch (IOException e) {
            System.err.println("Imported favourites and folders, but could not remove them from config.json: "
                    + e.getMessage());
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

    @Override
    public void close() throws SQLException {
        connection.close();
    }
}
