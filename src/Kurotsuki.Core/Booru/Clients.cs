using System.Text.Json;
using System.Xml.Linq;

namespace Kurotsuki.Core.Booru;

public sealed partial class GelbooruFamilyClient(HttpClient http, string baseUrl, string? apiKey, string? userId) : IBooruClient
{
    private readonly ApiRequest _api = new(http, baseUrl);

    public async Task<IReadOnlyList<Post>> SearchPostsAsync(string? tags, int page, int limit, CancellationToken ct = default)
    {
        var url = _api.Url("index.php", ("page", "dapi"), ("s", "post"), ("q", "index"), ("api_key", apiKey),
            ("user_id", userId), ("limit", limit), ("pid", page), ("tags", tags), ("json", 1));
        var body = await _api.GetAsync(url, ct);
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var items = root.ValueKind == JsonValueKind.Array ? root : root.Prop("post") ?? default;
        return items.ValueKind == JsonValueKind.Array ? items.EnumerateArray().Select(ToPost).ToList() : [];
    }

    private Post ToPost(JsonElement json) => new()
    {
        Id = json.Long("id"),
        Score = json.Int("score"),
        Width = json.Int("width"),
        Height = json.Int("height"),
        Image = json.Str("image"),
        Rating = json.Str("rating"),
        Source = json.Str("source"),
        Tags = json.Str("tags"),
        PreviewUrl = json.Str("preview_url"),
        FileUrl = json.Str("file_url"),
        SampleUrl = json.Str("sample_url"),
        ParentId = json.Long("parent_id"),
        HasChildren = json.Bool("has_children"),
        BooruBaseUrl = baseUrl,
    };

    public async Task<IReadOnlyList<string>> SuggestTagsAsync(string prefix, int limit, CancellationToken ct = default)
    {
        var url = _api.Url("index.php", ("page", "dapi"), ("s", "tag"), ("q", "index"), ("api_key", apiKey),
            ("user_id", userId), ("name_pattern", prefix + "%"), ("limit", limit), ("orderby", "count"),
            ("order", "DESC"), ("json", 1));
        var body = (await _api.GetAsync(url, ct)).Trim();
        if (body.Length == 0)
        {
            return [];
        }
        if (body.StartsWith('<'))
        {
            return XDocument.Parse(body).Descendants("tag").Select(t => (string?)t.Attribute("name") ?? "")
                .Where(n => n.Length > 0).ToList();
        }
        using var doc = JsonDocument.Parse(body);
        var tags = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement : doc.RootElement.Prop("tag") ?? default;
        return tags.ValueKind == JsonValueKind.Array
            ? tags.EnumerateArray().Select(t => t.Str("name")).OfType<string>().ToList() : [];
    }
}

public sealed partial class DanbooruClient(HttpClient http, string baseUrl, string? login, string? apiKey) : IBooruClient
{
    private readonly ApiRequest _api = new(http, baseUrl);

    public async Task<IReadOnlyList<Post>> SearchPostsAsync(string? tags, int page, int limit, CancellationToken ct = default)
    {
        var url = _api.Url("posts.json", ("login", login), ("api_key", apiKey), ("tags", tags), ("page", page + 1),
            ("limit", limit));
        var body = await _api.GetAsync(url, ct);
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().Select(ToPost).ToList() : [];
    }

    public Task<TagInfo?> TagInfoAsync(string tag, CancellationToken ct = default) =>
        TagInfoFetcher.FetchAsync(_api, tag, ct, [("login", login), ("api_key", apiKey)], []);

    private Post ToPost(JsonElement json)
    {
        var md5 = json.Str("md5");
        var ext = json.Str("file_ext");
        var post = new Post
        {
            Id = json.Long("id"),
            Score = json.Int("score"),
            Width = json.Int("image_width"),
            Height = json.Int("image_height"),
            Image = md5 is not null && ext is not null ? $"{md5}.{ext}" : null,
            Rating = json.Str("rating") switch
            {
                "g" => "general", "s" => "sensitive", "q" => "questionable", "e" => "explicit", var r => r,
            },
            Source = json.Str("source"),
            Tags = json.Str("tag_string"),
            PreviewUrl = json.Str("preview_file_url"),
            FileUrl = json.Str("file_url"),
            SampleUrl = json.Str("large_file_url"),
            ParentId = json.Long("parent_id"),
            HasChildren = json.Bool("has_children"),
            BooruBaseUrl = baseUrl,
        };
        post.AddTagCategories(json.Str("tag_string_artist"), TagCategory.Artist);
        post.AddTagCategories(json.Str("tag_string_copyright"), TagCategory.Copyright);
        post.AddTagCategories(json.Str("tag_string_character"), TagCategory.Character);
        post.AddTagCategories(json.Str("tag_string_general"), TagCategory.General);
        post.AddTagCategories(json.Str("tag_string_meta"), TagCategory.Meta);
        return post;
    }

    public async Task<IReadOnlyList<string>> SuggestTagsAsync(string prefix, int limit, CancellationToken ct = default)
    {
        var url = _api.Url("tags.json", ("login", login), ("api_key", apiKey), ("search[name_matches]", prefix + "*"),
            ("search[order]", "count"), ("limit", limit));
        var body = await _api.GetAsync(url, ct);
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray().Select(t => t.Str("name")).OfType<string>().ToList() : [];
    }
}

public sealed partial class E621Client(HttpClient http, string baseUrl, string? login, string? apiKey) : IBooruClient
{
    private readonly ApiRequest _api = new(http, baseUrl);
    private readonly string? _auth = BooruHttp.Basic(login, apiKey);

    public async Task<IReadOnlyList<Post>> SearchPostsAsync(string? tags, int page, int limit, CancellationToken ct = default)
    {
        var url = _api.Url("posts.json", ("tags", tags), ("page", page + 1), ("limit", limit));
        var body = await _api.GetAsync(url, ct, ("Authorization", _auth));
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Prop("posts").Items().Select(ToPost).ToList();
    }

    public async Task<IReadOnlyList<string>> SuggestTagsAsync(string prefix, int limit, CancellationToken ct = default)
    {
        var url = _api.Url("tags.json", ("search[name_matches]", prefix + "*"), ("search[order]", "count"), ("limit", limit));
        var body = await _api.GetAsync(url, ct, ("Authorization", _auth));
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray().Select(t => t.Str("name")).OfType<string>().ToList() : [];
    }

    public Task<TagInfo?> TagInfoAsync(string tag, CancellationToken ct = default) =>
        TagInfoFetcher.FetchAsync(_api, tag, ct, [], [("Authorization", _auth)]);

    private Post ToPost(JsonElement json)
    {
        var file = json.Prop("file") ?? default;
        var post = new Post
        {
            Id = json.Long("id"),
            Width = file.Int("width"),
            Height = file.Int("height"),
            Image = file.Str("md5") is { } md5 ? $"{md5}.{file.Str("ext")}" : null,
            FileUrl = UrlOf(file),
            PreviewUrl = UrlOf(json.Prop("preview") ?? default),
            SampleUrl = UrlOf(json.Prop("sample") ?? default),
            Score = (json.Prop("score") ?? default).Int("total"),
            Rating = BooruHttp.Rating(json.Str("rating")),
            Source = json.Prop("sources").Items().Select(s => s.GetString()).FirstOrDefault(),
            ParentId = (json.Prop("relationships") ?? default).Long("parent_id"),
            HasChildren = (json.Prop("relationships") ?? default).Bool("has_children"),
            BooruBaseUrl = baseUrl,
        };
        var names = new List<string>();
        if (json.Prop("tags") is { ValueKind: JsonValueKind.Object } categories)
        {
            foreach (var category in categories.EnumerateObject())
            {
                var list = category.Value.EnumerateArray().Select(t => t.GetString()!).ToList();
                names.AddRange(list);
                post.AddTagCategories(string.Join(' ', list), EnumInfo.TagCategoryFromName(category.Name));
            }
        }
        post.Tags = string.Join(' ', names);
        post.PoolIds.AddRange(json.Prop("pools").Items().Select(p => p.GetInt64()));
        return post;
    }

    private string? UrlOf(JsonElement obj) => BooruHttp.Absolute(baseUrl, obj.Str("url"));
}

public sealed partial class MoebooruClient(HttpClient http, string baseUrl) : IBooruClient
{
    private readonly ApiRequest _api = new(http, baseUrl);

    public async Task<IReadOnlyList<Post>> SearchPostsAsync(string? tags, int page, int limit, CancellationToken ct = default)
    {
        var body = await _api.GetAsync(_api.Url("post.json", ("tags", tags), ("page", page + 1), ("limit", limit)), ct);
        return Array(body).Select(ToPost).ToList();
    }

    public async Task<IReadOnlyList<string>> SuggestTagsAsync(string prefix, int limit, CancellationToken ct = default)
    {
        var body = await _api.GetAsync(_api.Url("tag.json", ("name", prefix + "*"), ("order", "count"), ("limit", limit)), ct);
        return Array(body).Select(t => t.Str("name")).OfType<string>().ToList();
    }

    private static List<JsonElement> Array(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList() : [];
    }

    private Post ToPost(JsonElement json)
    {
        var md5 = json.Str("md5");
        var ext = json.Str("file_ext");
        return new Post
        {
            Id = json.Long("id"),
            Score = json.Int("score"),
            Width = json.Int("width"),
            Height = json.Int("height"),
            Rating = BooruHttp.Rating(json.Str("rating")),
            Source = json.Str("source"),
            Tags = json.Str("tags"),
            PreviewUrl = BooruHttp.Absolute(baseUrl, json.Str("preview_url")),
            FileUrl = BooruHttp.Absolute(baseUrl, json.Str("file_url")),
            SampleUrl = BooruHttp.Absolute(baseUrl, json.Str("sample_url")),
            Image = md5 is not null && ext is not null ? $"{md5}.{ext}" : null,
            ParentId = json.Long("parent_id"),
            HasChildren = json.Bool("has_children"),
            BooruBaseUrl = baseUrl,
        };
    }
}

public sealed partial class ShimmieClient(HttpClient http, string baseUrl) : IBooruClient
{
    private readonly ApiRequest _api = new(http, baseUrl);

    public async Task<IReadOnlyList<Post>> SearchPostsAsync(string? tags, int page, int limit, CancellationToken ct = default)
    {
        var body = await _api.GetAsync(_api.Url("api/danbooru/find_posts", ("tags", tags), ("page", page + 1), ("limit", limit)), ct);
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }
        return XDocument.Parse(body).Descendants("post").Select(ToPost).ToList();
    }

    public Task<IReadOnlyList<string>> SuggestTagsAsync(string prefix, int limit, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    private Post ToPost(XElement e)
    {
        static int Int(string? v) => int.TryParse(v, out var n) ? n : 0;
        return new Post
        {
            Id = long.Parse((string?)e.Attribute("id") ?? "0"),
            Score = Int((string?)e.Attribute("score")),
            Width = Int((string?)e.Attribute("width")),
            Height = Int((string?)e.Attribute("height")),
            Rating = BooruHttp.Rating((string?)e.Attribute("rating")),
            Source = (string?)e.Attribute("source"),
            Tags = (string?)e.Attribute("tags"),
            PreviewUrl = BooruHttp.Absolute(baseUrl, (string?)e.Attribute("preview_url")),
            FileUrl = BooruHttp.Absolute(baseUrl, (string?)e.Attribute("file_url")),
            Image = (string?)e.Attribute("file_name"),
            BooruBaseUrl = baseUrl,
        };
    }
}

public sealed partial class SzurubooruClient(HttpClient http, string baseUrl, string? username, string? token) : IBooruClient
{
    private readonly ApiRequest _api = new(http, baseUrl);
    private readonly string? _auth = BooruHttp.Token(username, token);

    public async Task<IReadOnlyList<Post>> SearchPostsAsync(string? tags, int page, int limit, CancellationToken ct = default)
    {
        var body = await _api.GetAsync(_api.Url("api/posts/", ("offset", page * limit), ("limit", limit), ("query", tags ?? "")),
            ct, ("Authorization", _auth), ("Accept", "application/json"));
        return Results(body).Select(ToPost).ToList();
    }

    public async Task<IReadOnlyList<string>> SuggestTagsAsync(string prefix, int limit, CancellationToken ct = default)
    {
        var body = await _api.GetAsync(_api.Url("api/tags/", ("offset", 0), ("limit", limit), ("query", $"name:{prefix}* sort:usages")),
            ct, ("Authorization", _auth), ("Accept", "application/json"));
        return Results(body).Select(t => t.Prop("names").Items().Select(n => n.GetString()).FirstOrDefault())
            .OfType<string>().ToList();
    }

    private static List<JsonElement> Results(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Prop("results").Items().Select(e => e.Clone()).ToList();
    }

    private Post ToPost(JsonElement json) => new()
    {
        Id = json.Long("id"),
        Score = json.Int("score"),
        Width = json.Int("canvasWidth"),
        Height = json.Int("canvasHeight"),
        Rating = json.Str("safety") switch
        {
            "safe" => "general", "sketchy" => "questionable", "unsafe" => "explicit", var s => s,
        },
        Source = json.Str("source"),
        Tags = string.Join(' ', json.Prop("tags").Items().Select(t => t.ValueKind == JsonValueKind.String
            ? t.GetString() : t.Prop("names").Items().Select(n => n.GetString()).FirstOrDefault())),
        PreviewUrl = BooruHttp.Absolute(baseUrl, json.Str("thumbnailUrl")),
        FileUrl = BooruHttp.Absolute(baseUrl, json.Str("contentUrl")),
        BooruBaseUrl = baseUrl,
    };
}

public sealed partial class HydrusClient(HttpClient http, string baseUrl, string? accessKey) : IBooruClient
{
    private const int TagServiceTypeLocal = 5;
    private const int IdBatch = 1000;
    private static readonly Dictionary<string, List<long>> IdCache = new();
    private static readonly LinkedList<string> IdCacheOrder = new();

    private readonly string _base = baseUrl.TrimEnd('/');
    private readonly ApiRequest _api = new(http, baseUrl);

    public async Task<IReadOnlyList<Post>> SearchPostsAsync(string? tags, int page, int limit, CancellationToken ct = default)
    {
        var from = page * limit;
        var upTo = from + limit;
        var query = (tags ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
        var all = await FileIdsAsync(query, from, upTo, ct);
        if (all.Count <= from)
        {
            return [];
        }
        var ids = all.Skip(from).Take(limit).ToList();
        var body = await _api.GetAsync(_api.Url("get_files/file_metadata", ("file_ids", Serialize(ids))), ct,
            ("Hydrus-Client-API-Access-Key", accessKey));
        using var doc = JsonDocument.Parse(body);
        var services = LocalTagServices(doc.RootElement);
        return doc.RootElement.Prop("metadata").Items().Select(m => ToPost(m, services)).ToList();
    }

    // Hydrus can only return "the first N ids", so page N would cost O(N): fetch in batches and keep recent queries.
    private async Task<List<long>> FileIdsAsync(List<string> query, int from, int upTo, CancellationToken ct)
    {
        var key = _base + "|" + string.Join(' ', query);
        if (from > 0)
        {
            lock (IdCache)
            {
                if (IdCache.TryGetValue(key, out var cached) && (cached.Count >= upTo || cached.Count % IdBatch != 0))
                {
                    return cached;
                }
            }
        }
        var fetchLimit = (upTo + IdBatch - 1) / IdBatch * IdBatch;
        var search = new List<string>(query) { $"system:limit={fetchLimit}" };
        var body = await _api.GetAsync(_api.Url("get_files/search_files", ("tags", Serialize(search))), ct,
            ("Hydrus-Client-API-Access-Key", accessKey));
        using var doc = JsonDocument.Parse(body);
        var ids = doc.RootElement.Prop("file_ids").Items().Select(i => i.GetInt64()).ToList();
        lock (IdCache)
        {
            if (IdCache.Remove(key))
            {
                IdCacheOrder.Remove(key);
            }
            IdCache[key] = ids;
            IdCacheOrder.AddLast(key);
            while (IdCacheOrder.Count > 4)
            {
                IdCache.Remove(IdCacheOrder.First!.Value);
                IdCacheOrder.RemoveFirst();
            }
        }
        return ids;
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Config.ConfigManager.Json);

    /// <summary>Hydrus autocomplete (<c>/add_tags/search_tags</c>): tags that start with what was typed, most used first.</summary>
    public async Task<IReadOnlyList<string>> SuggestTagsAsync(string prefix, int limit, CancellationToken ct = default)
    {
        var body = await _api.GetAsync(_api.Url("add_tags/search_tags", ("search", prefix)), ct,
            ("Hydrus-Client-API-Access-Key", accessKey));
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Prop("tags").Items()
            .OrderByDescending(t => t.Long("count"))
            .Select(t => t.Str("value")).OfType<string>().Take(limit).ToList();
    }

    private static List<string> LocalTagServices(JsonElement root) =>
        root.Prop("services") is { ValueKind: JsonValueKind.Object } services
            ? services.EnumerateObject().Where(s => s.Value.Int("type") == TagServiceTypeLocal).Select(s => s.Name).ToList()
            : [];

    private Post ToPost(JsonElement file, List<string> localServices)
    {
        var fileId = file.Long("file_id");
        var hash = file.Str("hash") ?? "";
        var ext = (file.Str("ext") ?? "").TrimStart('.');
        var tags = new List<string>();
        if (file.Prop("tags") is { ValueKind: JsonValueKind.Object } byService)
        {
            foreach (var key in localServices)
            {
                if (byService.Prop(key)?.Prop("0")?.Prop("display_tags") is { } display)
                {
                    foreach (var tag in display.EnumerateArray().Select(t => t.GetString()!).Where(t => !tags.Contains(t)))
                    {
                        tags.Add(tag);
                    }
                }
            }
        }
        var post = new Post
        {
            Id = fileId,
            Width = file.Int("width"),
            Height = file.Int("height"),
            Image = ext.Length == 0 ? hash : $"{hash}.{ext}",
            // The access key is deliberately not part of these URLs: they are persisted and used as cache keys.
            // RequestAuth adds it as a header when the file is fetched.
            PreviewUrl = $"{_base}/get_files/thumbnail?file_id={fileId}",
            FileUrl = $"{_base}/get_files/file?file_id={fileId}",
            Tags = string.Join(' ', tags),
            BooruBaseUrl = _base,
        };
        foreach (var tag in tags)
        {
            post.AddTagCategories(tag, CategoryOf(tag));
        }
        return post;
    }

    internal static TagCategory CategoryOf(string tag)
    {
        var colon = tag.IndexOf(':');
        return colon <= 0 ? TagCategory.General : tag[..colon].ToLowerInvariant() switch
        {
            "creator" => TagCategory.Artist,
            "series" => TagCategory.Copyright,
            "character" => TagCategory.Character,
            _ => TagCategory.General,
        };
    }
}
