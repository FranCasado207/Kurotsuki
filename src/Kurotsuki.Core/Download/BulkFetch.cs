using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Net;

namespace Kurotsuki.Core.Download;

/// <summary>Collects the posts of several result pages for a bulk download, with a hard cap and a pause between pages.</summary>
public static class BulkFetch
{
    /// <summary>Never collect more than this many posts in one go.</summary>
    public const int MaxPosts = 1000;
    public const int MaxPages = 50;
    /// <summary>Between two search requests to the same booru.</summary>
    public static readonly TimeSpan PageSpacing = TimeSpan.FromSeconds(1);

    /// <param name="Truncated">Stopped because of the post cap.</param>
    /// <param name="Error">The failure that stopped the run, if any (what was read so far is kept).</param>
    public sealed record Result(List<Post> Posts, bool Truncated, string? Error, int PagesRead);

    /// <summary>
    /// Reads pages <paramref name="firstPage"/>..<paramref name="lastPage"/> (zero-based, inclusive). Stops early on a short
    /// page (the end of the results), a failure, cancellation or the cap.
    /// </summary>
    public static async Task<Result> FetchAsync(Func<int, Task<IReadOnlyList<Post>>> source, int firstPage, int lastPage,
        int pageSize, int maxPosts, Func<Post, bool> keep, HostThrottle throttle, string host,
        IProgress<int>? onPage = null, CancellationToken ct = default)
    {
        var limit = Math.Min(maxPosts, MaxPosts);
        var last = Math.Min(lastPage, firstPage + MaxPages - 1);
        var posts = new List<Post>();
        var seen = new HashSet<string>();
        var read = 0;
        for (var page = firstPage; page <= last; page++)
        {
            IReadOnlyList<Post> received;
            try
            {
                await throttle.AcquireAsync(host, ct);
                received = await source(page);
            }
            catch (OperationCanceledException)
            {
                return new Result(posts, false, null, read);
            }
            catch (Exception e)
            {
                return new Result(posts, false, e.Message, read);
            }
            read++;
            onPage?.Report(read);
            foreach (var post in received)
            {
                if (!keep(post) || !seen.Add(post.Key))
                {
                    continue;
                }
                if (posts.Count >= limit)
                {
                    return new Result(posts, true, null, read);
                }
                posts.Add(post);
            }
            if (received.Count < pageSize)
            {
                break;
            }
        }
        return new Result(posts, false, null, read);
    }
}
