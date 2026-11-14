namespace Kurotsuki.Core.Net;

public interface IMediaDownloader
{
    /// <param name="spaced">Pause between requests to the same host: for bulk file downloads, not thumbnails.</param>
    Task<string> DownloadAsync(string url, string target, IProgress<double>? progress = null, bool spaced = false,
        CancellationToken ct = default);
}
