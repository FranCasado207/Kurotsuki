using Kurotsuki.Core.Config;

namespace Kurotsuki.Core.Booru;

public sealed class BooruClientFactory(HttpClient http)
{
    /// <summary>The API client for the HTTP settings the JavaFX app used: 15 s to connect, 30 s to answer.</summary>
    public static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(15),
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        AllowAutoRedirect = true,
    })
    { Timeout = TimeSpan.FromSeconds(30) };

    public IBooruClient ClientFor(BooruConfiguration booru)
    {
        var engine = booru.EffectiveEngine ?? throw new InvalidOperationException("No engine selected for this booru.");
        var baseUrl = booru.BaseUrl!.Trim();
        return engine switch
        {
            BooruEngine.GelbooruFamily => new GelbooruFamilyClient(http, baseUrl, booru.ApiKey, booru.UserId),
            BooruEngine.DanbooruSoftware => new DanbooruClient(http, baseUrl, booru.UserId, booru.ApiKey),
            BooruEngine.MoebooruShimmie => booru.EffectiveFlavor switch
            {
                BooruFlavor.Shimmie => new ShimmieClient(http, baseUrl),
                BooruFlavor.Moebooru => new MoebooruClient(http, baseUrl),
                _ => throw new InvalidOperationException("Choose a flavor (Moebooru or Shimmie) for this booru."),
            },
            BooruEngine.E621 => new E621Client(http, baseUrl, booru.UserId, booru.ApiKey),
            BooruEngine.Hydrus => new HydrusClient(http, baseUrl, booru.ApiKey),
            BooruEngine.Szurubooru => new SzurubooruClient(http, baseUrl, booru.UserId, booru.ApiKey),
            _ => throw new InvalidOperationException($"Unsupported engine {engine}"),
        };
    }
}
