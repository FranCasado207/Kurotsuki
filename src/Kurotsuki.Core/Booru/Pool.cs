using System.Text.Json;

namespace Kurotsuki.Core.Booru;

/// <summary>An ordered collection of posts (a comic, a series). <c>PostIds</c> is the reading order. Same JSON as the Java record.</summary>
public sealed record Pool(long Id, string? Name, string? Description, string? Category, int PostCount,
    IReadOnlyList<long> PostIds, string? BooruBaseUrl, string? CreatedAt, string? UpdatedAt)
{
    public const string Series = "series";
    public const string Collection = "collection";

    /// <summary>e621 stores names with underscores.</summary>
    public string DisplayName => Name is null ? $"Pool #{Id}" : Name.Replace('_', ' ');

    public string Key => $"{BooruBaseUrl}#pool:{Id}";

    public bool IsSeries => string.Equals(Category, Series, StringComparison.OrdinalIgnoreCase);
}

public enum PoolOrder
{
    Updated,
    Name,
    Created,
    Size,
}

public static class PoolOrders
{
    public static string ApiValue(this PoolOrder order) => order switch
    {
        PoolOrder.Name => "name",
        PoolOrder.Created => "created_at",
        PoolOrder.Size => "post_count",
        _ => "updated_at",
    };

    public static string LabelKey(this PoolOrder order) => order switch
    {
        PoolOrder.Name => "pools.order.name",
        PoolOrder.Created => "pools.order.created",
        PoolOrder.Size => "pools.order.size",
        _ => "pools.order.updated",
    };
}

/// <summary>Helpers shared by the boorus that implement pools.</summary>
internal static class Pools
{
    private const int CoverBatch = 40;

    /// <summary>Parses the pool objects of Danbooru and e621, which share one JSON shape.</summary>
    public static List<Pool> ParseList(string? body, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray().Select(e => Parse(e, baseUrl)).ToList() : [];
    }

    public static Pool? ParseOne(string? body, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.ValueKind == JsonValueKind.Object ? Parse(doc.RootElement, baseUrl) : null;
    }

    public static Pool Parse(JsonElement json, string baseUrl)
    {
        var ids = json.Prop("post_ids").Items().Select(i => i.GetInt64()).ToList();
        var count = json.Prop("post_count") is { ValueKind: JsonValueKind.Number } c ? c.GetInt32() : ids.Count;
        return new Pool(json.Long("id"), json.Str("name"), json.Str("description"), json.Str("category"), count, ids,
            baseUrl, json.Str("created_at"), json.Str("updated_at"));
    }

    /// <summary>
    /// Posts of a pool fetched with the <c>pool:ID</c> search, which pages in no particular order; they are put
    /// back in the pool's own order and posts the account cannot see are skipped.
    /// </summary>
    public static async Task<IReadOnlyList<Post>> OrderedFromSearchAsync(IBooruClient client, Pool pool, int pageLimit,
        int maxPages, CancellationToken ct)
    {
        var byId = new Dictionary<long, Post>();
        for (var page = 0; page < maxPages && byId.Count < pool.PostIds.Count; page++)
        {
            var received = await client.SearchPostsAsync($"pool:{pool.Id}", page, pageLimit, ct);
            if (received.Count == 0)
            {
                break;
            }
            foreach (var post in received)
            {
                byId[post.Id] = post;
            }
        }
        return pool.PostIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }

    /// <summary>First post of every pool, with a few <c>id:a,b,c</c> searches (one request per 40 pools).</summary>
    public static async Task<IReadOnlyDictionary<long, Post>> CoversFromSearchAsync(IBooruClient client,
        IReadOnlyList<Pool> pools, CancellationToken ct)
    {
        var firstPostOf = pools.Where(p => p.PostIds.Count > 0).ToDictionary(p => p.Id, p => p.PostIds[0]);
        var posts = new Dictionary<long, Post>();
        foreach (var chunk in firstPostOf.Values.Chunk(CoverBatch))
        {
            foreach (var post in await client.SearchPostsAsync("id:" + string.Join(',', chunk), 0, chunk.Length, ct))
            {
                posts[post.Id] = post;
            }
        }
        return firstPostOf.Where(e => posts.ContainsKey(e.Value)).ToDictionary(e => e.Key, e => posts[e.Value]);
    }
}
