using System.Text;

namespace Kurotsuki.Core.Booru;

/// <summary>Small helper shared by the clients: builds a URL (null query values are left out, as JAX-RS did) and GETs it.</summary>
internal sealed class ApiRequest(HttpClient http, string baseUrl)
{
    public string BaseUrl { get; } = baseUrl.TrimEnd('/');

    public string Url(string path, params (string Key, object? Value)[] query)
    {
        var sb = new StringBuilder(BaseUrl).Append('/').Append(path.TrimStart('/'));
        var first = true;
        foreach (var (key, value) in query)
        {
            if (value is null)
            {
                continue;
            }
            sb.Append(first ? '?' : '&').Append(Uri.EscapeDataString(key)).Append('=')
                .Append(Uri.EscapeDataString(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!));
            first = false;
        }
        return sb.ToString();
    }

    public async Task<string> GetAsync(string url, CancellationToken ct, params (string Name, string? Value)[] headers)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", BooruHttp.UserAgent);
        foreach (var (name, value) in headers)
        {
            if (value is not null)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var code = (int)response.StatusCode;
            // Some boorus answer anonymous API calls with 401/403/503; say what to try instead of just the number.
            var message = code is 401 or 403 or 503
                ? I18n.Messages.Get("next.accessDenied", code)
                : $"HTTP {code} from {new Uri(url).Host}";
            throw new HttpRequestException(message, null, response.StatusCode);
        }
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }
}
