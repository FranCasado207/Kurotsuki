using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Kurotsuki.Core.Net;

namespace Kurotsuki.Core.Download;

/// <summary>
/// Thumbnail/media cache on disk. File names are the lowercase hex SHA-256 of the URL, the same scheme as the
/// Java CacheManager, so both apps reuse each other's cache. Concurrent requests for one URL share one download.
/// </summary>
public sealed partial class ImageCache(Func<string?> customFolder, string defaultFolder, IMediaDownloader downloader)
{
    // Static: the cache folder is shared by everything in the process, and two fetches of one URL must not write the same .part file.
    private static readonly ConcurrentDictionary<string, Lazy<Task<string>>> _inFlight = new();

    public string Folder()
    {
        var custom = customFolder();
        var folder = string.IsNullOrWhiteSpace(custom) ? defaultFolder : custom;
        Directory.CreateDirectory(folder);
        return folder;
    }

    public static string Hash(string url) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)));

    public Task<string> FetchAsync(string url, CancellationToken ct = default)
    {
        var path = Path.Combine(Folder(), Hash(url));
        if (File.Exists(path))
        {
            return Task.FromResult(path);
        }
        var lazy = _inFlight.GetOrAdd(path, p => new Lazy<Task<string>>(async () =>
        {
            try
            {
                return await downloader.DownloadAsync(url, p);
            }
            finally
            {
                _inFlight.TryRemove(p, out _);
            }
        }));
        return lazy.Value.WaitAsync(ct);
    }

    public const long DefaultMaxBytes = 2L << 30;
    private static readonly TimeSpan StalePart = TimeSpan.FromHours(1);

    /// <summary>Only files that look like ours are ever touched, so a cache folder shared with other things stays safe.</summary>
    [GeneratedRegex(@"^[0-9a-f]{64}(\.part(\.meta)?)?$|^[0-9a-f]{64}\.[a-z0-9]+(\.part(\.meta)?)?$")]
    private static partial Regex CacheFile();

    private IEnumerable<FileInfo> Files() =>
        new DirectoryInfo(Folder()).EnumerateFiles().Where(f => CacheFile().IsMatch(f.Name));

    public long SizeBytes() => Files().Sum(f => f.Length);

    /// <summary>Removes stale partial downloads and, if the cache exceeds <paramref name="maxBytes"/>, the oldest files first.</summary>
    public void Trim(long maxBytes = DefaultMaxBytes)
    {
        var staleBefore = DateTime.UtcNow - StalePart;
        var complete = new List<FileInfo>();
        foreach (var file in Files().ToList())
        {
            if (file.Name.Contains(".part"))
            {
                if (file.LastWriteTimeUtc < staleBefore)
                {
                    TryDelete(file);
                }
            }
            else
            {
                complete.Add(file);
            }
        }
        var total = complete.Sum(f => f.Length);
        foreach (var file in complete.OrderBy(f => f.LastAccessTimeUtc > f.LastWriteTimeUtc ? f.LastAccessTimeUtc : f.LastWriteTimeUtc))
        {
            if (total <= maxBytes)
            {
                break;
            }
            total -= file.Length;
            TryDelete(file);
        }
    }

    /// <summary>Deletes every cache file; returns how many.</summary>
    public int Clear()
    {
        var count = 0;
        foreach (var file in Files().ToList())
        {
            if (TryDelete(file))
            {
                count++;
            }
        }
        return count;
    }

    private static bool TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
