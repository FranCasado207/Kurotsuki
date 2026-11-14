using System.Text.Json;

namespace Kurotsuki.Core.Booru;

/// <param name="Wiki">The wiki page body as the booru stores it (DText), or null.</param>
/// <param name="AliasOf">Tags this one is an alias of (the booru treats them as the same thing).</param>
/// <param name="Aliases">Other spellings that count as this tag.</param>
/// <param name="Implies">Tags every post with this tag also gets.</param>
/// <param name="ImpliedBy">Tags that make a post get this one.</param>
public sealed record TagInfo(string Tag, string? Wiki, IReadOnlyList<string> AliasOf, IReadOnlyList<string> Aliases,
    IReadOnlyList<string> Implies, IReadOnlyList<string> ImpliedBy)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Wiki) && AliasOf.Count + Aliases.Count + Implies.Count + ImpliedBy.Count == 0;

    /// <summary>The wiki text with the DText link markup flattened, so it reads as plain text.</summary>
    public string? PlainWiki => Wiki is null ? null : System.Text.RegularExpressions.Regex
        .Replace(System.Text.RegularExpressions.Regex.Replace(Wiki, @"\[\[(?:[^\]|]*\|)?([^\]]*)\]\]", "$1"), @"\[/?[a-z]+(?:=[^\]]*)?\]", "")
        .Replace("\r\n", "\n").Trim();
}

/// <summary>Danbooru and e621 share these endpoints (wiki_pages, tag_aliases, tag_implications).</summary>
internal static class TagInfoFetcher
{
    public static async Task<TagInfo?> FetchAsync(ApiRequest api, string tag, CancellationToken ct,
        (string Key, object? Value)[] authQuery, (string Name, string? Value)[] headers)
    {
        async Task<JsonDocument?> Get(string path, params (string Key, object? Value)[] query)
        {
            var body = await api.GetAsync(api.Url(path, [.. query, .. authQuery]), ct, headers);
            return string.IsNullOrWhiteSpace(body) ? null : JsonDocument.Parse(body);
        }

        IEnumerable<string> Names(JsonDocument? doc, string field) =>
            doc is null ? [] : ((JsonElement?)doc.RootElement).Items().Select(e => e.Str(field)).OfType<string>().ToList();

        var wiki = Get("wiki_pages.json", ("search[title]", tag), ("limit", 1));
        var aliasOf = Get("tag_aliases.json", ("search[antecedent_name]", tag), ("search[status]", "active"), ("limit", 20));
        var aliases = Get("tag_aliases.json", ("search[consequent_name]", tag), ("search[status]", "active"), ("limit", 50));
        var implies = Get("tag_implications.json", ("search[antecedent_name]", tag), ("search[status]", "active"), ("limit", 50));
        var impliedBy = Get("tag_implications.json", ("search[consequent_name]", tag), ("search[status]", "active"), ("limit", 50));
        await Task.WhenAll(wiki, aliasOf, aliases, implies, impliedBy);
        using var w = await wiki;
        using var a1 = await aliasOf;
        using var a2 = await aliases;
        using var i1 = await implies;
        using var i2 = await impliedBy;
        var info = new TagInfo(tag, w is null ? null : ((JsonElement?)w.RootElement).Items().Select(e => e.Str("body")).FirstOrDefault(),
            [.. Names(a1, "consequent_name")], [.. Names(a2, "antecedent_name")],
            [.. Names(i1, "consequent_name")], [.. Names(i2, "antecedent_name")]);
        return info.IsEmpty ? null : info;
    }
}

/// <summary>Links that search for an image on other sites (the URL is the image's public address).</summary>
public static class ReverseImageSearch
{
    public static readonly IReadOnlyList<(string Name, Func<string, string> Url)> Engines =
    [
        ("SauceNAO", u => "https://saucenao.com/search.php?url=" + Uri.EscapeDataString(u)),
        ("IQDB", u => "https://iqdb.org/?url=" + Uri.EscapeDataString(u)),
        ("Google Lens", u => "https://lens.google.com/uploadbyurl?url=" + Uri.EscapeDataString(u)),
        ("TinEye", u => "https://tineye.com/search?url=" + Uri.EscapeDataString(u)),
        ("ascii2d", u => "https://ascii2d.net/search/url/" + Uri.EscapeDataString(u)),
    ];

    /// <summary>The address to hand to those sites: the sample if there is one (smaller), else the file, else the preview. http(s) only.</summary>
    public static string? ImageUrl(Post post) =>
        new[] { post.SampleUrl, post.FileUrl, post.PreviewUrl }
            .FirstOrDefault(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https");
}
