using System.Text.Json.Serialization;
using Kurotsuki.Core.Booru;

namespace Kurotsuki.Core.Config;

[JsonConverter(typeof(BooruConfigurationConverter))]
public sealed class BooruConfiguration
{
    public BooruTemplate? Template { get; set; }
    public BooruEngine? Engine { get; set; }
    public BooruFlavor? Flavor { get; set; }
    public string? CustomBaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public string? UserId { get; set; }

    // The JSON keeps the raw fields above (a template-based entry has no engine); these resolve them.
    [JsonIgnore] public BooruEngine? EffectiveEngine => Template?.Engine() ?? Engine;
    [JsonIgnore] public BooruFlavor? EffectiveFlavor => Template is { } t ? t.Flavor() : Flavor;

    [JsonIgnore]
    public string? BaseUrl => CustomBaseUrl ?? Template?.DefaultBaseUrl();

    [JsonIgnore]
    public string KindLabel => Template?.Label() ?? Engine?.Label() ?? "Custom";

    [JsonIgnore]
    public string DisplayName => Template is null ? $"{KindLabel} ({HostOrBase})" : KindLabel
        + (CustomBaseUrl is null ? "" : $" ({HostOrBase})");

    /// <summary>What the booru picker shows: the template's name, or just the host for a custom booru.</summary>
    [JsonIgnore]
    public string ShortName => Template is { } t && CustomBaseUrl is null ? t.Label() : HostOrBase;

    /// <summary>Tells boorus apart across runs (saved tabs point at one); no credentials in it.</summary>
    [JsonIgnore]
    public string Identity => $"{Template}|{Engine}|{Flavor}|{BaseUrl?.Trim()}";

    private string HostOrBase => Uri.TryCreate(BaseUrl?.Trim(), UriKind.Absolute, out var uri) ? uri.Host : BaseUrl ?? "?";

    public void Validate()
    {
        var baseUrl = BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException($"Booru without baseUrl: {KindLabel}");
        }
        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Malformed baseUrl for {KindLabel}: {baseUrl}");
        }
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException($"baseUrl of {KindLabel} must be an http(s) URL: {baseUrl}");
        }
    }

    /// <summary>True when credentials are configured for a non-local host reached over unencrypted http.</summary>
    public bool SendsCredentialsInClear()
    {
        var hasCredentials = !string.IsNullOrWhiteSpace(ApiKey) || !string.IsNullOrWhiteSpace(UserId);
        var uri = new Uri(BaseUrl!.Trim());
        var local = uri.Host is "localhost" or "127.0.0.1" or "[::1]" or "::1";
        return hasCredentials && uri.Scheme == Uri.UriSchemeHttp && !local;
    }
}
