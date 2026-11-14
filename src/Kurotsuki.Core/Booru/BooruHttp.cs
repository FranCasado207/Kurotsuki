using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Kurotsuki.Core.Booru;

public static class BooruHttp
{
    public static readonly string UserAgent =
        $"kurotsuki/{Version()} (desktop booru client; +https://github.com/FranCasado207/Kurotsuki)";

    private static string Version() =>
        typeof(BooruHttp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0] ?? "dev";

    public static string? Absolute(string baseUrl, string? url)
    {
        if (string.IsNullOrEmpty(url) || url.StartsWith("http://") || url.StartsWith("https://"))
        {
            return url;
        }
        var b = baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
        return b + url.TrimStart('/');
    }

    public static string? Basic(string? user, string? secret) =>
        user is null || secret is null ? null : "Basic " + Encode($"{user}:{secret}");

    public static string? Token(string? user, string? token) =>
        user is null || token is null ? null : "Token " + Encode($"{user}:{token}");

    public static string? Rating(string? letter) => letter switch
    {
        null => null,
        "s" => "general",
        "q" => "questionable",
        "e" => "explicit",
        _ => letter,
    };

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    /// <summary>The page a post lives on, or null when the engine has none (Hydrus).</summary>
    public static string? PageUrl(BooruEngine? engine, BooruFlavor? flavor, string? baseUrl, long id)
    {
        if (engine is null || baseUrl is null)
        {
            return null;
        }
        var b = baseUrl.TrimEnd('/');
        return engine switch
        {
            BooruEngine.DanbooruSoftware or BooruEngine.E621 => $"{b}/posts/{id}",
            BooruEngine.GelbooruFamily => $"{b}/index.php?page=post&s=view&id={id}",
            BooruEngine.Szurubooru => $"{b}/post/{id}",
            BooruEngine.Hydrus => null,
            BooruEngine.MoebooruShimmie => flavor == BooruFlavor.Shimmie ? $"{b}/post/view/{id}" : $"{b}/post/show/{id}",
            _ => null,
        };
    }

    /// <summary>A pool's page on the booru, or null for engines without pools.</summary>
    public static string? PoolUrl(BooruEngine? engine, BooruFlavor? flavor, string? baseUrl, long id)
    {
        if (engine is null || baseUrl is null)
        {
            return null;
        }
        var b = baseUrl.TrimEnd('/');
        return engine switch
        {
            BooruEngine.DanbooruSoftware or BooruEngine.E621 => $"{b}/pools/{id}",
            BooruEngine.MoebooruShimmie when flavor == BooruFlavor.Moebooru => $"{b}/pool/show/{id}",
            _ => null,
        };
    }

    /// <summary>Referer sent with media requests: the booru's own site when the URL is on a known one.</summary>
    public static string? RefererFor(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }
        foreach (var template in Enum.GetValues<BooruTemplate>())
        {
            var domain = new Uri(template.DefaultBaseUrl()).Host;
            if (uri.Host == domain || uri.Host.EndsWith("." + domain))
            {
                return template.DefaultBaseUrl() + "/";
            }
        }
        return new Uri(uri, "/").ToString();
    }
}

/// <summary>Tolerant accessors for JSON from servers that mix numbers, strings and nulls.</summary>
internal static class JsonExt
{
    public static JsonElement? Prop(this JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null
            ? v : null;

    public static string? Str(this JsonElement el, string name) => el.Prop(name) is { } v
        ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString() : null;

    public static long Long(this JsonElement el, string name) => el.Prop(name) is { } v
        ? v.ValueKind == JsonValueKind.Number ? v.GetInt64() : long.TryParse(v.ToString(), out var n) ? n : 0 : 0;

    /// <summary>True for JSON true and for the strings "true"/"1" some boorus use.</summary>
    public static bool Bool(this JsonElement el, string name) => el.Prop(name) is { } v
        && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.String && (v.GetString() is "true" or "1")
            || v.ValueKind == JsonValueKind.Number && v.GetInt64() != 0);

    public static int Int(this JsonElement el, string name) => (int)el.Long(name);

    public static IEnumerable<JsonElement> Items(this JsonElement? el) =>
        el is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray() : [];
}
