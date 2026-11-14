using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Config;

namespace Kurotsuki.Core.Net;

/// <summary>
/// Adds per-booru credentials to raw media requests (thumbnails, files) so secrets never have to be embedded in
/// the URLs we store, cache-key or log.
/// </summary>
public sealed class RequestAuth(Func<IReadOnlyList<BooruConfiguration>?> boorus)
{
    public const string HydrusHeader = "Hydrus-Client-API-Access-Key";

    public void Apply(HttpRequestMessage request, string? url)
    {
        if (boorus() is not { } configured || url is null)
        {
            return;
        }
        foreach (var booru in configured)
        {
            if (booru.EffectiveEngine == BooruEngine.Hydrus && !string.IsNullOrWhiteSpace(booru.ApiKey)
                && IsUnder(url, booru.BaseUrl))
            {
                request.Headers.TryAddWithoutValidation(HydrusHeader, booru.ApiKey);
                return;
            }
        }
    }

    public static bool IsUnder(string url, string? baseUrl)
    {
        if (baseUrl is null)
        {
            return false;
        }
        return url.StartsWith(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/", StringComparison.Ordinal);
    }
}
