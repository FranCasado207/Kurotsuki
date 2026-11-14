using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kurotsuki.Core.Booru;

// Pools and tag-category lookups, added to the clients that support them (same endpoints as the Java clients).

public sealed partial class E621Client
{
    private const int PoolPageLimit = 320;
    private const int MaxPoolPages = 30;

    public bool SupportsPools => true;

    public async Task<IReadOnlyList<Pool>> SearchPoolsAsync(string? nameContains, string? category, PoolOrder order,
        int page, int limit, CancellationToken ct = default)
    {
        var matches = string.IsNullOrWhiteSpace(nameContains) ? null : "*" + nameContains.Trim().Replace(' ', '_') + "*";
        var url = _api.Url("pools.json", ("search[name_matches]", matches), ("search[category]", category),
            ("search[order]", order.ApiValue()), ("page", page + 1), ("limit", limit));
        return Pools.ParseList(await _api.GetAsync(url, ct, ("Authorization", _auth)), baseUrl);
    }

    public async Task<Pool?> GetPoolAsync(long id, CancellationToken ct = default) =>
        Pools.ParseOne(await _api.GetAsync(_api.Url($"pools/{id}.json"), ct, ("Authorization", _auth)), baseUrl);

    public async Task<IReadOnlyList<Pool>> PoolsOfAsync(Post post, CancellationToken ct = default)
    {
        var ids = post.PoolIds;
        if (ids.Count == 0)
        {
            // Posts reloaded from the library do not remember their pools, so ask the server.
            var body = await _api.GetAsync(_api.Url($"posts/{post.Id}.json"), ct, ("Authorization", _auth));
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var doc = JsonDocument.Parse(body);
                ids = (doc.RootElement.Prop("post")?.Prop("pools")).Items().Select(i => i.GetInt64()).ToList();
            }
        }
        var pools = new List<Pool>();
        foreach (var id in ids)
        {
            if (await GetPoolAsync(id, ct) is { } pool)
            {
                pools.Add(pool);
            }
        }
        return pools;
    }

    // e621 returns at most PoolPageLimit posts per request; the page cap also stops a misbehaving server.
    public Task<IReadOnlyList<Post>> PoolPostsAsync(Pool pool, CancellationToken ct = default) =>
        Pools.OrderedFromSearchAsync(this, pool, PoolPageLimit, MaxPoolPages, ct);

    public Task<IReadOnlyDictionary<long, Post>> PoolCoversAsync(IReadOnlyList<Pool> pools, CancellationToken ct = default) =>
        Pools.CoversFromSearchAsync(this, pools, ct);
}

public sealed partial class DanbooruClient
{
    private const int PoolPageLimit = 200;
    private const int MaxPoolPages = 30;

    public bool SupportsPools => true;

    public async Task<IReadOnlyList<Pool>> SearchPoolsAsync(string? nameContains, string? category, PoolOrder order,
        int page, int limit, CancellationToken ct = default)
    {
        var matches = string.IsNullOrWhiteSpace(nameContains) ? null : "*" + nameContains.Trim().Replace(' ', '_') + "*";
        var url = _api.Url("pools.json", ("login", login), ("api_key", apiKey), ("search[name_matches]", matches),
            ("search[category]", category), ("search[order]", order.ApiValue()), ("page", page + 1), ("limit", limit));
        return Pools.ParseList(await _api.GetAsync(url, ct), baseUrl);
    }

    public async Task<Pool?> GetPoolAsync(long id, CancellationToken ct = default) =>
        Pools.ParseOne(await _api.GetAsync(_api.Url($"pools/{id}.json", ("login", login), ("api_key", apiKey)), ct), baseUrl);

    public async Task<IReadOnlyList<Pool>> PoolsOfAsync(Post post, CancellationToken ct = default)
    {
        // The post JSON has no pool ids; the pool search accepts a post query.
        var url = _api.Url("pools.json", ("login", login), ("api_key", apiKey), ("search[post_tags_match]", $"id:{post.Id}"), ("limit", 20));
        return Pools.ParseList(await _api.GetAsync(url, ct), baseUrl).Where(p => p.PostIds.Contains(post.Id)).ToList();
    }

    public Task<IReadOnlyList<Post>> PoolPostsAsync(Pool pool, CancellationToken ct = default) =>
        Pools.OrderedFromSearchAsync(this, pool, PoolPageLimit, MaxPoolPages, ct);

    public Task<IReadOnlyDictionary<long, Post>> PoolCoversAsync(IReadOnlyList<Pool> pools, CancellationToken ct = default) =>
        Pools.CoversFromSearchAsync(this, pools, ct);
}

public sealed partial class MoebooruClient
{
    private static readonly Regex PoolNotice = new("id=\"pool(\\d+)\"", RegexOptions.Compiled);

    public bool SupportsPools => true;

    /// <summary>Moebooru has no categories or sort orders for pools, so those arguments are ignored.</summary>
    public bool SupportsPoolFilters => false;

    /// <summary>The server pages pools in a fixed size that differs between sites, so only an empty page is the end.</summary>
    public int PoolPageSize(int requested) => 1;

    public async Task<IReadOnlyList<Pool>> SearchPoolsAsync(string? nameContains, string? category, PoolOrder order,
        int page, int limit, CancellationToken ct = default)
    {
        var query = string.IsNullOrWhiteSpace(nameContains) ? null : nameContains.Trim();
        var body = await _api.GetAsync(_api.Url("pool.json", ("query", query), ("page", page + 1)), ct);
        return Array(body).Select(e => ToPool(e, [])).ToList();
    }

    public async Task<Pool?> GetPoolAsync(long id, CancellationToken ct = default) => (await ShowPoolAsync(id, ct)).Pool;

    public async Task<IReadOnlyList<Pool>> PoolsOfAsync(Post post, CancellationToken ct = default)
    {
        // The JSON API does not say which pools a post is in; its web page does.
        var html = await _api.GetAsync(_api.Url($"post/show/{post.Id}"), ct);
        var pools = new List<Pool>();
        foreach (var id in PoolIdsInPage(html))
        {
            if (await GetPoolAsync(id, ct) is { } pool)
            {
                pools.Add(pool);
            }
        }
        return pools;
    }

    /// <summary>Pool ids from the "this post is in a pool" notices of a post page.</summary>
    internal static List<long> PoolIdsInPage(string? html) =>
        html is null ? [] : PoolNotice.Matches(html).Select(m => long.Parse(m.Groups[1].Value)).Distinct().ToList();

    public async Task<IReadOnlyList<Post>> PoolPostsAsync(Pool pool, CancellationToken ct = default) =>
        (await ShowPoolAsync(pool.Id, ct)).Posts;

    private async Task<(Pool? Pool, List<Post> Posts)> ShowPoolAsync(long id, CancellationToken ct)
    {
        // Returns the pool with all of its posts in reading order; there is no paging.
        var body = await _api.GetAsync(_api.Url("pool/show.json", ("id", id)), ct);
        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, []);
        }
        using var doc = JsonDocument.Parse(body);
        var posts = doc.RootElement.Prop("posts").Items().Select(ToPost).ToList();
        return (ToPool(doc.RootElement, posts.Select(p => p.Id).ToList()), posts);
    }

    private Pool ToPool(JsonElement json, List<long> postIds)
    {
        var count = json.Prop("post_count") is { ValueKind: JsonValueKind.Number } c ? c.GetInt32() : postIds.Count;
        return new Pool(json.Long("id"), json.Str("name"), json.Str("description"), null, count, postIds, baseUrl,
            json.Str("created_at"), json.Str("updated_at"));
    }

    public async Task<IReadOnlyDictionary<string, TagCategory>> TagCategoriesAsync(IEnumerable<string> names, CancellationToken ct = default)
    {
        var categories = new Dictionary<string, TagCategory>();
        foreach (var name in names)
        {
            var body = await _api.GetAsync(_api.Url("tag.json", ("name", name), ("order", "count"), ("limit", 100)), ct);
            if (Array(body).FirstOrDefault(t => t.Str("name") == name) is { ValueKind: JsonValueKind.Object } tag)
            {
                categories[name] = tag.Int("type") switch
                {
                    0 => TagCategory.General, 1 => TagCategory.Artist, 3 => TagCategory.Copyright,
                    4 => TagCategory.Character, _ => TagCategory.Other,
                };
            }
        }
        return categories;
    }
}

public sealed partial class GelbooruFamilyClient
{
    public async Task<IReadOnlyDictionary<string, TagCategory>> TagCategoriesAsync(IEnumerable<string> names, CancellationToken ct = default)
    {
        var categories = new Dictionary<string, TagCategory>();
        foreach (var name in names)
        {
            var url = _api.Url("index.php", ("page", "dapi"), ("s", "tag"), ("q", "index"), ("api_key", apiKey),
                ("user_id", userId), ("name_pattern", name), ("limit", 10), ("json", 1));
            var body = (await _api.GetAsync(url, ct)).Trim();
            foreach (var (tagName, type) in TypesByName(body))
            {
                if (tagName == name)
                {
                    categories[name] = type switch
                    {
                        0 => TagCategory.General, 1 => TagCategory.Artist, 3 => TagCategory.Copyright,
                        4 => TagCategory.Character, 5 => TagCategory.Meta, _ => TagCategory.Other,
                    };
                }
            }
        }
        return categories;
    }

    private static IEnumerable<(string Name, int Type)> TypesByName(string body)
    {
        if (body.Length == 0)
        {
            yield break;
        }
        if (body.StartsWith('<'))
        {
            foreach (var tag in System.Xml.Linq.XDocument.Parse(body).Descendants("tag"))
            {
                if (tag.Attribute("type") is { } type && int.TryParse(type.Value, out var n))
                {
                    yield return ((string?)tag.Attribute("name") ?? "", n);
                }
            }
            yield break;
        }
        using var doc = JsonDocument.Parse(body);
        var tags = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement : doc.RootElement.Prop("tag") ?? default;
        foreach (var tag in tags.ValueKind == JsonValueKind.Array ? tags.EnumerateArray().ToList() : [])
        {
            if (tag.Prop("type") is not null)
            {
                yield return (tag.Str("name") ?? "", tag.Int("type"));
            }
        }
    }
}

public sealed partial class SzurubooruClient
{
    public async Task<IReadOnlyDictionary<string, TagCategory>> TagCategoriesAsync(IEnumerable<string> names, CancellationToken ct = default)
    {
        var categories = new Dictionary<string, TagCategory>();
        foreach (var name in names)
        {
            var body = await _api.GetAsync(_api.Url("api/tags/", ("offset", 0), ("limit", 5), ("query", $"name:{name}")),
                ct, ("Authorization", _auth), ("Accept", "application/json"));
            foreach (var tag in Results(body))
            {
                if (tag.Prop("names").Items().Any(n => n.GetString() == name))
                {
                    categories[name] = (tag.Str("category") ?? "default") switch
                    {
                        "default" => TagCategory.General, "artist" => TagCategory.Artist,
                        "copyright" => TagCategory.Copyright, "character" => TagCategory.Character,
                        "meta" => TagCategory.Meta, _ => TagCategory.Other,
                    };
                    break;
                }
            }
        }
        return categories;
    }
}
