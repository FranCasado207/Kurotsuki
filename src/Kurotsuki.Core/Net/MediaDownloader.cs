using System.Globalization;
using System.Net;
using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Logging;

namespace Kurotsuki.Core.Net;

/// <summary>
/// Downloads media files with timeouts, a size cap and a stall watchdog. Rate limits (429), server errors (5xx)
/// and dropped transfers are retried with exponential backoff, honouring <c>Retry-After</c>. An interrupted
/// transfer keeps its <c>.part</c> file and resumes with an HTTP <c>Range</c> request, but only when the server
/// gave an ETag or Last-Modified proving the file did not change in between. Same behaviour as the Java app.
/// </summary>
public sealed class MediaDownloader : IMediaDownloader
{
    public const long MaxDownloadBytes = 1L << 30;
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(60);

    private static readonly HttpClient DefaultClient = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(15),
        AllowAutoRedirect = true,
    })
    { Timeout = Timeout.InfiniteTimeSpan };

    private readonly HttpClient _client;
    private readonly RequestAuth _auth;
    private readonly TimeSpan _stallLimit;
    private readonly TimeSpan _backoffBase;
    private readonly int _maxAttempts;
    private readonly HostThrottle _throttle = new(TimeSpan.FromMilliseconds(200));

    public MediaDownloader(RequestAuth auth, TimeSpan? stallLimit = null, TimeSpan? backoffBase = null, int maxAttempts = 4,
        HttpClient? client = null)
    {
        _client = client ?? DefaultClient;
        _auth = auth;
        _stallLimit = stallLimit ?? TimeSpan.FromSeconds(30);
        _backoffBase = backoffBase ?? TimeSpan.FromSeconds(1);
        _maxAttempts = Math.Max(1, maxAttempts);
    }

    private sealed class Retryable(string message, TimeSpan? retryAfter, Exception? inner = null)
        : IOException(message, inner)
    {
        public TimeSpan? RetryAfter { get; } = retryAfter;
    }

    /// <param name="spaced">Pause between requests to the same host: for bulk file downloads, not thumbnails.</param>
    public async Task<string> DownloadAsync(string url, string target, IProgress<double>? progress = null,
        bool spaced = false, CancellationToken ct = default)
    {
        var partial = target + ".part";
        var meta = target + ".part.meta";
        var host = new Uri(url).Host;
        Retryable? last = null;
        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            try
            {
                if (spaced)
                {
                    await _throttle.AcquireAsync(host, ct).ConfigureAwait(false);
                }
                await AttemptOnceAsync(url, target, partial, meta, progress, ct).ConfigureAwait(false);
                progress?.Report(1.0);
                return target;
            }
            catch (Retryable e)
            {
                last = e;
                AppLog.Warning($"Retry {attempt}/{_maxAttempts}: {AppLog.SafeUrl(url)} {e.Message}");
                if (attempt < _maxAttempts)
                {
                    await Task.Delay(e.RetryAfter ?? Backoff(attempt), ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                throw; // keep the .part file: a later run can resume it
            }
            catch (Exception e) when (e is IOException or HttpRequestException)
            {
                Discard(partial, meta);
                AppLog.Warning($"Download failed: {AppLog.SafeUrl(url)} {e.Message}");
                throw;
            }
        }
        AppLog.Warning($"Download gave up after {_maxAttempts} attempts: {AppLog.SafeUrl(url)} {last!.Message}");
        throw new IOException(last!.Message, last);
    }

    internal TimeSpan Backoff(int attempt)
    {
        var millis = _backoffBase.TotalMilliseconds * Math.Pow(2, Math.Min(attempt - 1, 10));
        return TimeSpan.FromMilliseconds(Math.Min(millis, MaxBackoff.TotalMilliseconds));
    }

    /// <summary>Seconds or an HTTP date; null when absent, unparsable or already past.</summary>
    public static TimeSpan? ParseRetryAfter(string? value, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var trimmed = value.Trim();
        if (long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }
        if (DateTimeOffset.TryParseExact(trimmed, "r", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
        {
            return at > now ? at - now : TimeSpan.Zero;
        }
        return null;
    }

    private async Task AttemptOnceAsync(string url, string target, string partial, string meta,
        IProgress<double>? progress, CancellationToken ct)
    {
        long offset = 0;
        var validator = File.Exists(partial) && File.Exists(meta) ? (await File.ReadAllTextAsync(meta, ct)).Trim() : null;
        if (!string.IsNullOrEmpty(validator))
        {
            offset = new FileInfo(partial).Length;
        }
        if (offset == 0)
        {
            Discard(partial, meta);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", BooruHttp.UserAgent);
        if (BooruHttp.RefererFor(url) is { } referer)
        {
            request.Headers.TryAddWithoutValidation("Referer", referer);
        }
        _auth.Apply(request, url);
        if (offset > 0)
        {
            request.Headers.TryAddWithoutValidation("Range", $"bytes={offset}-");
            request.Headers.TryAddWithoutValidation("If-Range", validator);
        }

        using var headerTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        headerTimeout.CancelAfter(ResponseTimeout);
        HttpResponseMessage response;
        try
        {
            response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headerTimeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new Retryable("Timed out waiting for the server", null);
        }
        catch (HttpRequestException e)
        {
            throw new Retryable(e.Message, null, e);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            if (status == 416)
            {
                Discard(partial, meta);
                throw new Retryable("HTTP 416", TimeSpan.Zero);
            }
            if (status == 429 || status >= 500)
            {
                var wait = ParseRetryAfter(response.Headers.TryGetValues("Retry-After", out var v) ? v.FirstOrDefault() : null,
                    DateTimeOffset.UtcNow);
                if (wait > MaxRetryAfter)
                {
                    throw new IOException($"HTTP {status} (retry after {(int)wait.Value.TotalSeconds}s)");
                }
                throw new Retryable($"HTTP {status}", wait);
            }
            if (status != 200 && status != 206)
            {
                throw new IOException($"HTTP {status}");
            }
            var resumed = status == 206;
            if (resumed && response.Content.Headers.ContentRange?.From != offset)
            {
                Discard(partial, meta);
                throw new Retryable("Unexpected Content-Range", TimeSpan.Zero);
            }
            if (!resumed)
            {
                offset = 0;
            }
            var length = response.Content.Headers.ContentLength ?? -1;
            var total = length < 0 ? -1 : length + offset;
            if (total > MaxDownloadBytes)
            {
                throw new IOException($"File too large ({total} bytes)");
            }
            var newValidator = response.Headers.ETag?.ToString()
                ?? response.Content.Headers.LastModified?.ToString("r", CultureInfo.InvariantCulture);
            if (newValidator is not null && !newValidator.Contains('\n') && !newValidator.Contains('\r'))
            {
                await File.WriteAllTextAsync(meta, newValidator, ct);
            }
            else
            {
                File.Delete(meta);
            }
            try
            {
                await CopyAsync(response, partial, offset, total, progress, ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or HttpRequestException)
            {
                if (e is IOException { Message: "File too large" })
                {
                    throw;
                }
                if (newValidator is null)
                {
                    Discard(partial, meta);
                }
                throw new Retryable(e.Message, null, e);
            }
        }
        File.Move(partial, target, overwrite: true);
        File.Delete(meta);
    }

    private async Task CopyAsync(HttpResponseMessage response, string partial, long offset, long total,
        IProgress<double>? progress, CancellationToken ct)
    {
        // The header timeout only covers the headers; this aborts the body when no data arrives for the stall limit.
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var output = new FileStream(partial, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write);
        var buffer = new byte[64 * 1024];
        var transferred = offset;
        try
        {
            while (true)
            {
                stall.CancelAfter(_stallLimit);
                var read = await input.ReadAsync(buffer, stall.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
                transferred += read;
                if (transferred > MaxDownloadBytes)
                {
                    throw new IOException("File too large");
                }
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                if (total > 0)
                {
                    progress?.Report((double)transferred / total);
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new IOException("Transfer stalled");
        }
    }

    private static void Discard(string partial, string meta)
    {
        try
        {
            File.Delete(partial);
            File.Delete(meta);
        }
        catch (IOException) { }
    }
}
