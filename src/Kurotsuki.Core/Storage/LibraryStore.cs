using Kurotsuki.Core.Booru;
using Microsoft.Data.Sqlite;

namespace Kurotsuki.Core.Storage;

/// <summary>An unfinished download kept across restarts. <c>Kind</c> is "post" or "pool"; payload is JSON.</summary>
public sealed record QueueEntry(string Key, string Kind, string Payload);

/// <summary>Where the user is in a pool (0-based page) and whether they finished it.</summary>
public sealed record ReadingRecord(string PoolKey, string BooruBaseUrl, long PoolId, string? Name, string? Category,
    int Page, int Total, bool Finished, long UpdatedAt)
{
    public ReadingRecord With(int page, int total, bool finished, long time) =>
        this with { Page = page, Total = total, Finished = finished, UpdatedAt = time };
}

/// <summary>
/// <c>kurotsuki.db</c>, with the exact schema of the Java app's LibraryStore (both apps share the file), so
/// every table is created with <c>IF NOT EXISTS</c> and the <c>reading_progress</c> columns are added the same way.
/// One connection, guarded by a lock; operations are short.
/// </summary>
public sealed class LibraryStore : IDisposable
{
    private const string MigratedFlag = "migrated_from_json";
    private const int Batch = 400;

    private readonly SqliteConnection _connection;
    private readonly object _gate = new();

    public LibraryStore(string databaseFile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databaseFile))!);
        _connection = new SqliteConnection($"Data Source={databaseFile}");
        _connection.Open();
        CreateSchema();
    }

    private void CreateSchema()
    {
        Execute("PRAGMA foreign_keys = ON");
        Execute("CREATE TABLE IF NOT EXISTS meta (name TEXT PRIMARY KEY, value TEXT)");
        Execute("""
            CREATE TABLE IF NOT EXISTS posts (
                post_key TEXT PRIMARY KEY, booru_base_url TEXT, id INTEGER NOT NULL, score INTEGER, width INTEGER,
                height INTEGER, image TEXT, rating TEXT, source TEXT, tags TEXT, preview_url TEXT, file_url TEXT,
                sample_url TEXT)
            """);
        Execute("""
            CREATE TABLE IF NOT EXISTS post_tags (
                post_key TEXT NOT NULL REFERENCES posts(post_key) ON DELETE CASCADE,
                tag TEXT NOT NULL, category TEXT NOT NULL, PRIMARY KEY (post_key, tag))
            """);
        Execute("""
            CREATE TABLE IF NOT EXISTS tag_categories (
                booru_base_url TEXT NOT NULL, tag TEXT NOT NULL, category TEXT NOT NULL,
                PRIMARY KEY (booru_base_url, tag))
            """);
        Execute("CREATE TABLE IF NOT EXISTS favorite_tags (tag TEXT PRIMARY KEY)");
        Execute("CREATE TABLE IF NOT EXISTS favorite_posts (post_key TEXT PRIMARY KEY REFERENCES posts(post_key) ON DELETE CASCADE)");
        Execute("CREATE TABLE IF NOT EXISTS reading_progress (pool_key TEXT PRIMARY KEY, page INTEGER NOT NULL, updated_at INTEGER NOT NULL)");
        AddColumnIfMissing("reading_progress", "finished", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing("reading_progress", "total", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing("reading_progress", "name", "TEXT");
        AddColumnIfMissing("reading_progress", "booru_base_url", "TEXT");
        AddColumnIfMissing("reading_progress", "pool_id", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing("reading_progress", "category", "TEXT");
        Execute("CREATE TABLE IF NOT EXISTS tag_groups (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL UNIQUE)");
        Execute("""
            CREATE TABLE IF NOT EXISTS tag_group_tags (
                group_id INTEGER NOT NULL REFERENCES tag_groups(id) ON DELETE CASCADE,
                tag TEXT NOT NULL REFERENCES favorite_tags(tag) ON DELETE CASCADE, PRIMARY KEY (group_id, tag))
            """);
        Execute("CREATE TABLE IF NOT EXISTS folders (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL UNIQUE)");
        Execute("""
            CREATE TABLE IF NOT EXISTS folder_posts (
                folder_id INTEGER NOT NULL REFERENCES folders(id) ON DELETE CASCADE,
                post_key TEXT NOT NULL REFERENCES posts(post_key) ON DELETE CASCADE, PRIMARY KEY (folder_id, post_key))
            """);
        Execute("CREATE INDEX IF NOT EXISTS idx_folder_posts_post ON folder_posts(post_key)");
        Execute("CREATE TABLE IF NOT EXISTS downloaded (item_key TEXT PRIMARY KEY, saved_at INTEGER NOT NULL)");
        Execute("""
            CREATE TABLE IF NOT EXISTS download_queue (
                position INTEGER PRIMARY KEY AUTOINCREMENT, item_key TEXT NOT NULL UNIQUE, kind TEXT NOT NULL,
                payload TEXT NOT NULL)
            """);
    }

    private void AddColumnIfMissing(string table, string column, string definition)
    {
        // SQLite has no "ADD COLUMN IF NOT EXISTS".
        var exists = Query($"PRAGMA table_info({table})", r => r.GetString(1)).Any(n => n.Equals(column, StringComparison.OrdinalIgnoreCase));
        if (!exists)
        {
            Execute($"ALTER TABLE {table} ADD COLUMN {column} {definition}");
        }
    }

    // ---- downloads ------------------------------------------------------------------------------------------

    /// <summary>Keys (post or pool) that were saved to disk before, so they can be skipped next time.</summary>
    public HashSet<string> DownloadedKeys() => [.. Query("SELECT item_key FROM downloaded", r => r.GetString(0))];

    public void MarkDownloaded(string key, long savedAtMillis) =>
        Execute("INSERT OR REPLACE INTO downloaded (item_key, saved_at) VALUES ($k, $t)", ("$k", key), ("$t", savedAtMillis));

    public void ForgetDownloads() => Execute("DELETE FROM downloaded");

    public List<QueueEntry> QueueEntries() =>
        Query("SELECT item_key, kind, payload FROM download_queue ORDER BY position",
            r => new QueueEntry(r.GetString(0), r.GetString(1), r.GetString(2)));

    /// <summary>Replaces the saved queue. Entries of kinds this app does not know are never deleted (forward compatibility).</summary>
    public void SaveQueue(IEnumerable<QueueEntry> entries) => InTransaction(() =>
    {
        Execute("DELETE FROM download_queue WHERE kind IN ('post', 'pool')");
        foreach (var e in entries)
        {
            Execute("INSERT OR IGNORE INTO download_queue (item_key, kind, payload) VALUES ($k, $kind, $p)",
                ("$k", e.Key), ("$kind", e.Kind), ("$p", e.Payload));
        }
    });

    // ---- tag categories cache -------------------------------------------------------------------------------

    public Dictionary<string, TagCategory> TagCategories(string booruBaseUrl, IEnumerable<string> tags)
    {
        var result = new Dictionary<string, TagCategory>();
        foreach (var chunk in tags.Distinct().Chunk(Batch))
        {
            var names = Params("$t", chunk);
            foreach (var (tag, category) in Query(
                         $"SELECT tag, category FROM tag_categories WHERE booru_base_url = $b AND tag IN ({names.Sql})",
                         r => (r.GetString(0), r.GetString(1)), [("$b", booruBaseUrl), .. names.Args]))
            {
                if (Enum.TryParse<TagCategory>(category, true, out var parsed))
                {
                    result[tag] = parsed;
                }
            }
        }
        return result;
    }

    public void SaveTagCategories(string booruBaseUrl, IReadOnlyDictionary<string, TagCategory> categories) => InTransaction(() =>
    {
        foreach (var (tag, category) in categories)
        {
            Execute("INSERT OR REPLACE INTO tag_categories (booru_base_url, tag, category) VALUES ($b, $t, $c)",
                ("$b", booruBaseUrl), ("$t", tag), ("$c", Name(category)));
        }
    });

    // ---- favourite tags and groups --------------------------------------------------------------------------

    public List<string> FavoriteTags() => Query("SELECT tag FROM favorite_tags ORDER BY rowid", r => r.GetString(0));
    public void AddFavoriteTag(string tag) => Execute("INSERT OR IGNORE INTO favorite_tags (tag) VALUES ($t)", ("$t", tag));
    public void RemoveFavoriteTag(string tag) => Execute("DELETE FROM favorite_tags WHERE tag = $t", ("$t", tag));

    /// <summary>Group name to its tags, in creation order. Groups without tags are included.</summary>
    public Dictionary<string, List<string>> TagGroups()
    {
        var groups = new Dictionary<string, List<string>>();
        var order = new List<string>();
        foreach (var (name, tag) in Query("""
                     SELECT g.name, t.tag FROM tag_groups g LEFT JOIN tag_group_tags t ON t.group_id = g.id
                     ORDER BY g.id, t.rowid
                     """, r => (r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1))))
        {
            if (!groups.TryGetValue(name, out var list))
            {
                groups[name] = list = [];
                order.Add(name);
            }
            if (tag is not null)
            {
                list.Add(tag);
            }
        }
        return groups;
    }

    public void CreateTagGroup(string name) => Execute("INSERT OR IGNORE INTO tag_groups (name) VALUES ($n)", ("$n", name));
    public void RenameTagGroup(string name, string newName) => Execute("UPDATE tag_groups SET name = $new WHERE name = $n", ("$new", newName), ("$n", name));
    public void DeleteTagGroup(string name) => Execute("DELETE FROM tag_groups WHERE name = $n", ("$n", name));

    /// <summary>The tag must already be a favourite (enforced by a foreign key).</summary>
    public void AddTagToGroup(string group, string tag) =>
        Execute("INSERT OR IGNORE INTO tag_group_tags (group_id, tag) VALUES ((SELECT id FROM tag_groups WHERE name = $g), $t)",
            ("$g", group), ("$t", tag));

    public void RemoveTagFromGroup(string group, string tag) =>
        Execute("DELETE FROM tag_group_tags WHERE group_id = (SELECT id FROM tag_groups WHERE name = $g) AND tag = $t",
            ("$g", group), ("$t", tag));

    // ---- favourite posts and folders ------------------------------------------------------------------------

    public List<Post> FavoritePosts() => PostsFor(Query("SELECT post_key FROM favorite_posts ORDER BY rowid", r => r.GetString(0)));

    public void SetFavoritePost(Post post, bool favorite) => InTransaction(() =>
    {
        if (favorite)
        {
            SavePost(post);
            Execute("INSERT OR IGNORE INTO favorite_posts (post_key) VALUES ($k)", ("$k", post.Key));
        }
        else
        {
            Execute("DELETE FROM favorite_posts WHERE post_key = $k", ("$k", post.Key));
            PruneUnusedPosts();
        }
    });

    public bool IsFavoritePost(string key) =>
        Query("SELECT 1 FROM favorite_posts WHERE post_key = $k", r => 1, [("$k", key)]).Count > 0;

    public HashSet<string> FavoritePostKeys() => [.. Query("SELECT post_key FROM favorite_posts", r => r.GetString(0))];

    /// <summary>Folder name to its posts, in creation order.</summary>
    public Dictionary<string, List<Post>> Folders()
    {
        var keysByFolder = new Dictionary<string, List<string>>();
        foreach (var (name, key) in Query("""
                     SELECT f.name, fp.post_key FROM folders f LEFT JOIN folder_posts fp ON fp.folder_id = f.id
                     ORDER BY f.id, fp.rowid
                     """, r => (r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1))))
        {
            if (!keysByFolder.TryGetValue(name, out var list))
            {
                keysByFolder[name] = list = [];
            }
            if (key is not null)
            {
                list.Add(key);
            }
        }
        var byKey = PostsFor(keysByFolder.Values.SelectMany(k => k).Distinct().ToList()).ToDictionary(p => p.Key);
        return keysByFolder.ToDictionary(f => f.Key, f => f.Value.Where(byKey.ContainsKey).Select(k => byKey[k]).ToList());
    }

    public void CreateFolder(string name) => Execute("INSERT OR IGNORE INTO folders (name) VALUES ($n)", ("$n", name));

    public void DeleteFolder(string name) => InTransaction(() =>
    {
        Execute("DELETE FROM folders WHERE name = $n", ("$n", name));
        PruneUnusedPosts();
    });

    public void AddToFolder(string folder, Post post) => InTransaction(() =>
    {
        SavePost(post);
        Execute("INSERT OR IGNORE INTO folder_posts (folder_id, post_key) VALUES ((SELECT id FROM folders WHERE name = $f), $k)",
            ("$f", folder), ("$k", post.Key));
    });

    public void RemoveFromFolder(string folder, Post post) => InTransaction(() =>
    {
        Execute("DELETE FROM folder_posts WHERE folder_id = (SELECT id FROM folders WHERE name = $f) AND post_key = $k",
            ("$f", folder), ("$k", post.Key));
        PruneUnusedPosts();
    });

    // ---- reading progress -----------------------------------------------------------------------------------

    /// <summary>Every pool opened in the reader, newest first. Rows from older versions without a name are skipped.</summary>
    public List<ReadingRecord> ReadingRecords() => Query("""
        SELECT pool_key, booru_base_url, pool_id, name, category, page, total, finished, updated_at
        FROM reading_progress WHERE pool_id > 0 AND booru_base_url IS NOT NULL ORDER BY updated_at DESC
        """, r => new ReadingRecord(r.GetString(0), r.GetString(1), r.GetInt64(2), r.IsDBNull(3) ? null : r.GetString(3),
        r.IsDBNull(4) ? null : r.GetString(4), r.GetInt32(5), r.GetInt32(6), r.GetInt32(7) != 0, r.GetInt64(8)));

    public void SaveReading(ReadingRecord r) => Execute("""
        INSERT OR REPLACE INTO reading_progress (pool_key, booru_base_url, pool_id, name, category, page, total, finished, updated_at)
        VALUES ($k, $b, $id, $n, $c, $p, $t, $f, $u)
        """, ("$k", r.PoolKey), ("$b", r.BooruBaseUrl), ("$id", r.PoolId), ("$n", r.Name), ("$c", r.Category),
        ("$p", r.Page), ("$t", r.Total), ("$f", r.Finished ? 1 : 0), ("$u", r.UpdatedAt));

    public void DeleteReading(string poolKey) => Execute("DELETE FROM reading_progress WHERE pool_key = $k", ("$k", poolKey));

    // ---- one-time import from config.json (the Java app did this too) -----------------------------------------

    /// <summary>
    /// Favourites and folders used to live in <c>config.json</c>. Imports them once (flag committed with the data),
    /// then the caller clears them from the config. Returns true when something was imported.
    /// </summary>
    public bool MigrateFromConfig(IEnumerable<string>? tags, IEnumerable<Post>? favoritePosts,
        IEnumerable<(string Name, IEnumerable<Post> Posts)>? folders)
    {
        if (MetaValue(MigratedFlag) is not null)
        {
            return false;
        }
        InTransaction(() =>
        {
            foreach (var tag in tags ?? [])
            {
                AddFavoriteTag(tag);
            }
            foreach (var post in favoritePosts ?? [])
            {
                SetFavoritePost(post, true);
            }
            foreach (var (name, posts) in folders ?? [])
            {
                CreateFolder(name);
                foreach (var post in posts)
                {
                    AddToFolder(name, post);
                }
            }
            Execute("INSERT INTO meta (name, value) VALUES ($n, $v)", ("$n", MigratedFlag), ("$v", "1"));
        });
        return true;
    }

    private string? MetaValue(string name) =>
        Query("SELECT value FROM meta WHERE name = $n", r => r.IsDBNull(0) ? null : r.GetString(0), [("$n", name)]).FirstOrDefault();

    // ---- posts ----------------------------------------------------------------------------------------------

    private void SavePost(Post post)
    {
        Execute("""
            INSERT INTO posts (post_key, booru_base_url, id, score, width, height, image, rating, source, tags,
                               preview_url, file_url, sample_url)
            VALUES ($key, $b, $id, $score, $w, $h, $image, $rating, $source, $tags, $preview, $file, $sample)
            ON CONFLICT(post_key) DO UPDATE SET score = excluded.score, width = excluded.width, height = excluded.height,
                image = excluded.image, rating = excluded.rating, source = excluded.source, tags = excluded.tags,
                preview_url = excluded.preview_url, file_url = excluded.file_url, sample_url = excluded.sample_url
            """, ("$key", post.Key), ("$b", post.BooruBaseUrl), ("$id", post.Id), ("$score", post.Score),
            ("$w", post.Width), ("$h", post.Height), ("$image", post.Image), ("$rating", post.Rating),
            ("$source", post.Source), ("$tags", post.Tags), ("$preview", post.PreviewUrl), ("$file", post.FileUrl),
            ("$sample", post.SampleUrl));
        Execute("DELETE FROM post_tags WHERE post_key = $k", ("$k", post.Key));
        foreach (var tag in post.TagList.Distinct())
        {
            Execute("INSERT OR IGNORE INTO post_tags (post_key, tag, category) VALUES ($k, $t, $c)",
                ("$k", post.Key), ("$t", tag), ("$c", Name(post.CategoryOf(tag))));
        }
    }

    /// <summary>Loads posts in two queries per chunk of keys; keeps the order of <paramref name="keys"/>.</summary>
    private List<Post> PostsFor(List<string> keys)
    {
        var byKey = new Dictionary<string, Post>();
        foreach (var chunk in keys.Chunk(Batch))
        {
            var p = Params("$k", chunk);
            foreach (var (key, post) in Query($"""
                         SELECT post_key, booru_base_url, id, score, width, height, image, rating, source, tags,
                                preview_url, file_url, sample_url FROM posts WHERE post_key IN ({p.Sql})
                         """, r => (r.GetString(0), new Post
                {
                    BooruBaseUrl = Str(r, 1), Id = r.GetInt64(2), Score = r.GetInt32(3), Width = r.GetInt32(4),
                    Height = r.GetInt32(5), Image = Str(r, 6), Rating = Str(r, 7), Source = Str(r, 8), Tags = Str(r, 9),
                    PreviewUrl = Str(r, 10), FileUrl = Str(r, 11), SampleUrl = Str(r, 12),
                }), p.Args))
            {
                byKey[key] = post;
            }
            foreach (var (key, tag, category) in Query(
                         $"SELECT post_key, tag, category FROM post_tags WHERE post_key IN ({p.Sql})",
                         r => (r.GetString(0), r.GetString(1), r.GetString(2)), p.Args))
            {
                if (byKey.TryGetValue(key, out var post) && Enum.TryParse<TagCategory>(category, true, out var parsed))
                {
                    post.TagCategories[tag] = parsed;
                }
            }
        }
        return keys.Where(byKey.ContainsKey).Select(k => byKey[k]).ToList();
    }

    private void PruneUnusedPosts() => Execute("""
        DELETE FROM posts
        WHERE NOT EXISTS (SELECT 1 FROM favorite_posts f WHERE f.post_key = posts.post_key)
          AND NOT EXISTS (SELECT 1 FROM folder_posts p WHERE p.post_key = posts.post_key)
        """);

    // ---- plumbing -------------------------------------------------------------------------------------------

    private static string? Str(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    /// <summary>Java stored enum names (<c>ARTIST</c>); keep that format.</summary>
    private static string Name(TagCategory category) => category.ToString().ToUpperInvariant();

    private static (string Sql, (string, object?)[] Args) Params(string prefix, IEnumerable<string> values)
    {
        var args = values.Select((v, i) => ($"{prefix}{i}", (object?)v)).ToArray();
        return (string.Join(",", args.Select(a => a.Item1)), args);
    }

    private void InTransaction(Action work)
    {
        lock (_gate)
        {
            if (_transaction is not null)
            {
                work(); // nested call: the outer transaction commits
                return;
            }
            using var tx = _connection.BeginTransaction();
            _transaction = tx;
            try
            {
                work();
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
            finally
            {
                _transaction = null;
            }
        }
    }

    private SqliteTransaction? _transaction;

    private void Execute(string sql, params (string Name, object? Value)[] args)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText = sql;
            foreach (var (name, value) in args)
            {
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }
            command.ExecuteNonQuery();
        }
    }

    private List<T> Query<T>(string sql, Func<SqliteDataReader, T> read, (string Name, object? Value)[]? args = null)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText = sql;
            foreach (var (name, value) in args ?? [])
            {
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }
            using var reader = command.ExecuteReader();
            var rows = new List<T>();
            while (reader.Read())
            {
                rows.Add(read(reader));
            }
            return rows;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _connection.Dispose();
        }
    }
}
