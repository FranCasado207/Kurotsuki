using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Config;
using Kurotsuki.Core.Storage;

namespace Kurotsuki.Core.Backup;

/// <summary>
/// Exports and imports boorus, blacklist, favourites, folders, tag groups, reading progress and a few settings as one
/// JSON file shared with the Java app and Kurotsuki Mobile (format: docs/backup-format.md in the Java repository).
/// Importing only ever merges; nothing the user already has is removed.
/// </summary>
public static class BackupFile
{
    public const string Format = "kurotsuki-backup";
    public const int Version = 1;
    public const long MaxBytes = 50L * 1024 * 1024;
    private const int MaxText = 20_000;

    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>What an import changed or skipped, for showing to the user.</summary>
    public sealed record Summary(int Boorus, int Blacklist, int FavoriteTags, int TagGroups, int FavoritePosts,
        int FolderPosts, int Reading, int Skipped, bool Settings);

    public sealed class BackupException(string message) : Exception(message);

    // ---- export ---------------------------------------------------------------------------------------------

    public static string Export(UserConfiguration config, LibraryStore store, bool includeSecrets)
    {
        var root = new JsonObject
        {
            ["format"] = Format,
            ["version"] = Version,
            ["createdAt"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            ["app"] = "desktop",
            ["includesSecrets"] = includeSecrets,
        };

        var boorus = new JsonArray();
        foreach (var booru in config.BooruConfigurations)
        {
            var engine = EngineId(booru);
            var baseUrl = booru.BaseUrl;
            if (engine is null || string.IsNullOrWhiteSpace(baseUrl))
            {
                continue;
            }
            var json = new JsonObject { ["name"] = booru.KindLabel, ["engine"] = engine, ["baseUrl"] = NormalizeBaseUrl(baseUrl) };
            if (includeSecrets)
            {
                AddIfPresent(json, "login", booru.UserId);
                AddIfPresent(json, "apiKey", booru.ApiKey);
            }
            boorus.Add(json);
        }
        root["boorus"] = boorus;
        root["blacklist"] = new JsonArray(config.BlacklistedTags.Select(t => (JsonNode)TagBlacklist.Normalize(t)).ToArray());
        root["favoriteTags"] = new JsonArray(store.FavoriteTags().Select(t => (JsonNode)t).ToArray());
        root["tagGroups"] = new JsonArray(store.TagGroups().Select(g => (JsonNode)new JsonObject
        {
            ["name"] = g.Key,
            ["tags"] = new JsonArray(g.Value.Select(t => (JsonNode)t).ToArray()),
        }).ToArray());
        root["favoritePosts"] = new JsonArray(store.FavoritePosts().Select(PostToJson).ToArray());
        root["folders"] = new JsonArray(store.Folders().Select(f => (JsonNode)new JsonObject
        {
            ["name"] = f.Key,
            ["posts"] = new JsonArray(f.Value.Select(PostToJson).ToArray()),
        }).ToArray());

        var reading = new JsonArray();
        foreach (var r in store.ReadingRecords())
        {
            var json = new JsonObject
            {
                ["poolKey"] = r.PoolKey, ["booruBaseUrl"] = r.BooruBaseUrl, ["poolId"] = r.PoolId, ["name"] = r.Name,
            };
            AddIfPresent(json, "category", r.Category);
            json["page"] = r.Page;
            json["total"] = r.Total;
            json["finished"] = r.Finished;
            json["updatedAt"] = r.UpdatedAt;
            reading.Add(json);
        }
        root["reading"] = reading;

        var settings = new JsonObject();
        AddIfPresent(settings, "language", config.Language);
        AddIfPresent(settings, "poolArchiveFormat", config.PoolArchiveFormat);
        AddIfPresent(settings, "readerMode", config.ReaderMode);
        AddIfPresent(settings, "readerFit", config.ReaderFit);
        settings["readerRightToLeft"] = config.ReaderRightToLeft;
        settings["readerSpread"] = config.ReaderSpread;
        AddIfPresent(settings, "downloadTemplate", config.DownloadTemplate);
        AddIfPresent(settings, "downloadSidecar", config.DownloadSidecar);
        settings["redownloadKnown"] = config.RedownloadKnown;
        root["settings"] = settings;

        return root.ToJsonString(Pretty);
    }

    // ---- import ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Merges the backup into the configuration and the library. The caller saves the configuration afterwards; the
    /// library is written directly (services holding copies must reload).
    /// </summary>
    public static Summary Import(string json, UserConfiguration config, LibraryStore store)
    {
        if (string.IsNullOrEmpty(json) || json.Length > MaxBytes)
        {
            throw new BackupException("The file is empty or too large.");
        }
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new BackupException("This is not a Kurotsuki backup.");
        }
        if (root.ValueKind != JsonValueKind.Object || Str(root, "format") != Format)
        {
            throw new BackupException("This is not a Kurotsuki backup.");
        }
        var version = Int(root, "version");
        if (version is null || version > Version)
        {
            throw new BackupException($"This backup was made by a newer version ({version}).");
        }

        var skipped = 0;
        var boorusAdded = 0;
        var boorus = new List<BooruConfiguration>(config.BooruConfigurations);
        foreach (var entry in Objects(root, "boorus"))
        {
            if (ToBooru(entry) is not { } imported)
            {
                skipped++;
                continue;
            }
            if (Find(boorus, imported) is { } existing)
            {
                if (NotBlank(imported.UserId)) existing.UserId = imported.UserId;
                if (NotBlank(imported.ApiKey)) existing.ApiKey = imported.ApiKey;
            }
            else
            {
                boorus.Add(imported);
                boorusAdded++;
            }
        }
        config.BooruConfigurations = boorus;

        var blacklist = new List<string>(config.BlacklistedTags);
        var blacklistAdded = 0;
        foreach (var tag in Strings(root, "blacklist"))
        {
            var normalized = TagBlacklist.Normalize(tag);
            if (normalized.Length > 0 && !blacklist.Contains(normalized))
            {
                blacklist.Add(normalized);
                blacklistAdded++;
            }
        }
        config.BlacklistedTags = blacklist;

        var knownTags = new HashSet<string>(store.FavoriteTags());
        var tagsAdded = 0;
        foreach (var tag in Strings(root, "favoriteTags"))
        {
            if (knownTags.Add(tag))
            {
                store.AddFavoriteTag(tag);
                tagsAdded++;
            }
        }

        var existingGroups = store.TagGroups();
        var groupsTouched = 0;
        foreach (var group in Objects(root, "tagGroups"))
        {
            var name = Str(group, "name")?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                skipped++;
                continue;
            }
            if (!existingGroups.ContainsKey(name))
            {
                store.CreateTagGroup(name);
                existingGroups[name] = [];
                groupsTouched++;
            }
            foreach (var tag in Strings(group, "tags"))
            {
                if (knownTags.Add(tag))
                {
                    store.AddFavoriteTag(tag); // a group can only hold favourite tags
                    tagsAdded++;
                }
                if (!existingGroups[name].Contains(tag))
                {
                    store.AddTagToGroup(name, tag);
                    existingGroups[name].Add(tag);
                    groupsTouched++;
                }
            }
        }

        var favoriteKeys = store.FavoritePostKeys();
        var postsAdded = 0;
        foreach (var entry in Objects(root, "favoritePosts"))
        {
            if (ToPost(entry) is not { } post)
            {
                skipped++;
            }
            else if (favoriteKeys.Add(post.Key))
            {
                store.SetFavoritePost(post, true);
                postsAdded++;
            }
        }

        var existingFolders = store.Folders();
        var folderPostsAdded = 0;
        foreach (var folder in Objects(root, "folders"))
        {
            var name = Str(folder, "name")?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                skipped++;
                continue;
            }
            if (!existingFolders.ContainsKey(name))
            {
                store.CreateFolder(name);
                existingFolders[name] = [];
            }
            var inFolder = existingFolders[name].Select(p => p.Key).ToHashSet();
            foreach (var entry in Objects(folder, "posts"))
            {
                if (ToPost(entry) is not { } post)
                {
                    skipped++;
                }
                else if (inFolder.Add(post.Key))
                {
                    store.AddToFolder(name, post);
                    folderPostsAdded++;
                }
            }
        }

        var records = store.ReadingRecords().ToDictionary(r => r.PoolKey);
        var readingChanged = 0;
        foreach (var entry in Objects(root, "reading"))
        {
            if (ToReading(entry) is not { } record)
            {
                skipped++;
                continue;
            }
            if (!records.TryGetValue(record.PoolKey, out var current) || record.UpdatedAt > current.UpdatedAt)
            {
                store.SaveReading(record);
                records[record.PoolKey] = record;
                readingChanged++;
            }
        }

        var settings = root.TryGetProperty("settings", out var s) && s.ValueKind == JsonValueKind.Object ? s : default;
        var settingsChanged = settings.ValueKind == JsonValueKind.Object && ApplySettings(settings, config);

        return new Summary(boorusAdded, blacklistAdded, tagsAdded, groupsTouched, postsAdded, folderPostsAdded,
            readingChanged, skipped, settingsChanged);
    }

    private static bool ApplySettings(JsonElement settings, UserConfiguration config)
    {
        var changed = false;
        var language = Str(settings, "language");
        if (language is "en" or "pt") { config.Language = language; changed = true; }
        if (Str(settings, "poolArchiveFormat") is { } format) { config.PoolArchiveFormat = format; changed = true; }
        if (Str(settings, "readerMode") is { } mode) { config.ReaderMode = mode; changed = true; }
        if (Str(settings, "readerFit") is { } fit) { config.ReaderFit = fit; changed = true; }
        if (Bool(settings, "readerRightToLeft") is { } rtl) { config.ReaderRightToLeft = rtl; changed = true; }
        if (Bool(settings, "readerSpread") is { } spread) { config.ReaderSpread = spread; changed = true; }
        if (Str(settings, "downloadTemplate") is { } template) { config.DownloadTemplate = template; changed = true; }
        if (Str(settings, "downloadSidecar") is { } sidecar) { config.DownloadSidecar = sidecar; changed = true; }
        if (Bool(settings, "redownloadKnown") is { } redownload) { config.RedownloadKnown = redownload; changed = true; }
        return changed;
    }

    // ---- mapping --------------------------------------------------------------------------------------------

    internal static string? EngineId(BooruConfiguration booru) => booru.EffectiveEngine switch
    {
        BooruEngine.E621 => "e621",
        BooruEngine.DanbooruSoftware => "danbooru",
        BooruEngine.GelbooruFamily => "gelbooru",
        BooruEngine.Szurubooru => "szurubooru",
        BooruEngine.Hydrus => "hydrus",
        BooruEngine.MoebooruShimmie => booru.EffectiveFlavor == BooruFlavor.Shimmie ? "shimmie" : "moebooru",
        _ => null,
    };

    private static BooruConfiguration? ToBooru(JsonElement json)
    {
        var id = Str(json, "engine");
        var baseUrl = Str(json, "baseUrl");
        if (id is null || baseUrl is null)
        {
            return null;
        }
        if (!TryEngine(id, out var engine, out var flavor))
        {
            return null;
        }
        var url = NormalizeBaseUrl(baseUrl);
        var booru = new BooruConfiguration();
        if (TemplateFor(engine, flavor, url) is { } template)
        {
            booru.Template = template;
        }
        else
        {
            booru.Engine = engine;
            booru.Flavor = flavor;
            booru.CustomBaseUrl = url;
        }
        booru.UserId = BlankToNull(Str(json, "login"));
        booru.ApiKey = BlankToNull(Str(json, "apiKey"));
        try
        {
            booru.Validate();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        return booru;
    }

    private static bool TryEngine(string id, out BooruEngine engine, out BooruFlavor? flavor)
    {
        flavor = null;
        switch (id)
        {
            case "e621": engine = BooruEngine.E621; return true;
            case "danbooru": engine = BooruEngine.DanbooruSoftware; return true;
            case "gelbooru": engine = BooruEngine.GelbooruFamily; return true;
            case "szurubooru": engine = BooruEngine.Szurubooru; return true;
            case "hydrus": engine = BooruEngine.Hydrus; return true;
            case "moebooru": engine = BooruEngine.MoebooruShimmie; flavor = BooruFlavor.Moebooru; return true;
            case "shimmie": engine = BooruEngine.MoebooruShimmie; flavor = BooruFlavor.Shimmie; return true;
            default: engine = default; return false;
        }
    }

    private static BooruTemplate? TemplateFor(BooruEngine engine, BooruFlavor? flavor, string url)
    {
        foreach (var template in Enum.GetValues<BooruTemplate>())
        {
            if (template.Engine() == engine && template.Flavor() == flavor
                && NormalizeBaseUrl(template.DefaultBaseUrl()).Equals(url, StringComparison.OrdinalIgnoreCase))
            {
                return template;
            }
        }
        return null;
    }

    private static BooruConfiguration? Find(List<BooruConfiguration> boorus, BooruConfiguration wanted) =>
        boorus.FirstOrDefault(b => b.EffectiveEngine == wanted.EffectiveEngine && b.EffectiveFlavor == wanted.EffectiveFlavor
            && b.BaseUrl is not null
            && NormalizeBaseUrl(b.BaseUrl).Equals(NormalizeBaseUrl(wanted.BaseUrl), StringComparison.OrdinalIgnoreCase));

    internal static JsonNode PostToJson(Post post)
    {
        var json = new JsonObject
        {
            ["booruBaseUrl"] = NormalizeBaseUrl(post.BooruBaseUrl),
            ["id"] = post.Id, ["score"] = post.Score, ["width"] = post.Width, ["height"] = post.Height,
        };
        AddIfPresent(json, "image", post.Image);
        AddIfPresent(json, "rating", post.Rating);
        AddIfPresent(json, "source", post.Source);
        json["tags"] = post.Tags ?? "";
        AddIfPresent(json, "previewUrl", post.PreviewUrl);
        AddIfPresent(json, "fileUrl", post.FileUrl);
        AddIfPresent(json, "sampleUrl", post.SampleUrl);
        var categories = new JsonObject();
        foreach (var (tag, category) in post.TagCategories)
        {
            categories[tag] = category.ToString().ToLowerInvariant();
        }
        json["tagCategories"] = categories;
        return json;
    }

    internal static Post? ToPost(JsonElement json)
    {
        var baseUrl = Str(json, "booruBaseUrl");
        var id = Long(json, "id");
        if (baseUrl is null || id is not > 0 || !IsHttp(baseUrl))
        {
            return null;
        }
        var post = new Post
        {
            BooruBaseUrl = NormalizeBaseUrl(baseUrl),
            Id = id.Value,
            Score = Int(json, "score") ?? 0,
            Width = Int(json, "width") ?? 0,
            Height = Int(json, "height") ?? 0,
            Image = Str(json, "image"),
            Rating = Str(json, "rating"),
            Source = Str(json, "source"),
            Tags = string.Join(' ', (Str(json, "tags") ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)),
            PreviewUrl = HttpOrNull(Str(json, "previewUrl")),
            FileUrl = HttpOrNull(Str(json, "fileUrl")),
            SampleUrl = HttpOrNull(Str(json, "sampleUrl")),
        };
        if (json.TryGetProperty("tagCategories", out var cats) && cats.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in cats.EnumerateObject().Where(e => e.Value.ValueKind == JsonValueKind.String))
            {
                post.TagCategories[entry.Name] = EnumInfo.TagCategoryFromName(entry.Value.GetString()!.ToLowerInvariant());
            }
        }
        return post;
    }

    private static ReadingRecord? ToReading(JsonElement json)
    {
        var key = Str(json, "poolKey");
        var baseUrl = Str(json, "booruBaseUrl");
        var poolId = Long(json, "poolId");
        var page = Int(json, "page");
        var total = Int(json, "total");
        if (key is null || baseUrl is null || poolId is not > 0 || page is null or < 0 || total is null || !IsHttp(baseUrl))
        {
            return null;
        }
        return new ReadingRecord(key, NormalizeBaseUrl(baseUrl), poolId.Value, Str(json, "name") ?? $"Pool {poolId}",
            Str(json, "category"), page.Value, total.Value, Bool(json, "finished") == true, Long(json, "updatedAt") ?? 0);
    }

    // ---- small helpers --------------------------------------------------------------------------------------

    internal static string NormalizeBaseUrl(string? url) => (url ?? "").Trim().TrimEnd('/');

    private static bool IsHttp(string url) =>
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && uri.Host.Length > 0
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string? HttpOrNull(string? url) => url is not null && IsHttp(url) ? url.Trim() : null;
    private static string? BlankToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static bool NotBlank(string? value) => !string.IsNullOrWhiteSpace(value);

    private static void AddIfPresent(JsonObject json, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            json[key] = value;
        }
    }

    // typed readers: wrong types are "absent", not errors (a malformed entry is skipped, not fatal)
    private static string? Str(JsonElement json, string key) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? Truncate(v.GetString()) : null;

    private static string? Truncate(string? s) => s is not null && s.Length > MaxText ? s[..MaxText] : s;

    private static long? Long(JsonElement json, string key) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number
        && v.TryGetInt64(out var n) ? n : null;

    private static int? Int(JsonElement json, string key) => Long(json, key) is { } n && n is >= int.MinValue and <= int.MaxValue ? (int)n : null;

    private static bool? Bool(JsonElement json, string key) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean() : null;

    private static List<string> Strings(JsonElement json, string key) =>
        json.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString()))
                .Select(e => e.GetString()!.Trim()).ToList() : [];

    private static List<JsonElement> Objects(JsonElement json, string key) =>
        json.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).ToList() : [];
}
