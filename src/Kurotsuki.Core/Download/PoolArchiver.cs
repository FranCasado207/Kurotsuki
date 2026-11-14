using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Download.Comic;
using Kurotsuki.Core.I18n;

namespace Kurotsuki.Core.Download;

/// <summary>Saves a whole pool as one comic archive (cbz, zip, …) containing the pages and a ComicInfo.xml.</summary>
public sealed class PoolArchiver(Func<string?> configuredFormat, Func<Pool, PoolArchiver.Source?> sources,
    Func<string, CancellationToken, Task<string>> fetch)
{
    private static readonly HashSet<string> PageTypes = ["png", "jpg", "jpeg", "gif", "bmp", "webp"];
    private const int Prefetch = 4;
    private const double FetchShare = 0.8;

    /// <summary>The booru a pool lives on: where to ask for its posts and how to describe it in the metadata.</summary>
    public sealed record Source(IBooruClient Client, string BaseUrl, string Publisher);

    /// <summary>The format from the config file; throws if it holds something we cannot write.</summary>
    public ArchiveFormat ConfiguredFormat()
    {
        var configured = configuredFormat();
        if (string.IsNullOrWhiteSpace(configured))
        {
            return ArchiveFormat.Cbz;
        }
        var format = ArchiveFormat.Parse(configured)
            ?? throw new IOException(Messages.Get("download.formatUnknown", configured, string.Join(", ", ArchiveFormat.All.Select(f => f.Extension))));
        return format.CanWrite ? format : throw new IOException(Messages.Get("next.poolFormatUnsupported", format.Extension));
    }

    /// <summary>Returns the archive that was written.</summary>
    public async Task<string> ArchiveAsync(Pool pool, string directory, IProgress<double> progress, CancellationToken ct = default)
    {
        var format = ConfiguredFormat();
        var source = sources(pool) ?? throw new IOException(Messages.Get("pools.notSupported"));
        var posts = (await source.Client.PoolPostsAsync(pool, ct)).Where(IsPage).ToList();
        if (posts.Count == 0)
        {
            throw new IOException(Messages.Get("pools.postsEmpty"));
        }

        var width = Math.Max(3, posts.Count.ToString().Length);
        var entries = new List<ArchiveWriter.Entry>();
        var pages = new List<ComicInfo.Page>();
        var fetches = new List<Task<string>>();
        for (var i = 0; i < posts.Count; i++)
        {
            // Keep a few downloads in flight, so a long pool does not wait on the network page by page.
            for (var ahead = fetches.Count; ahead < Math.Min(posts.Count, i + Prefetch); ahead++)
            {
                fetches.Add(fetch(posts[ahead].FileUrl!, ct));
            }
            string file;
            try
            {
                file = await fetches[i];
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                throw new IOException(Messages.Get("download.pageFailed", i + 1, e.Message), e);
            }
            var ext = ImageSniffer.DetectExtension(file, ExtensionOf(posts[i]));
            entries.Add(new ArchiveWriter.Entry($"{(i + 1).ToString().PadLeft(width, '0')}.{ext}", file));
            pages.Add(new ComicInfo.Page(posts[i], new FileInfo(file).Length));
            progress.Report(FetchShare * (i + 1) / posts.Count);
        }

        var baseUrl = source.BaseUrl.TrimEnd('/');
        var xml = ComicInfo.Xml(pool, pages, $"{baseUrl}/pools/{pool.Id}", source.Publisher, "Kurotsuki-Next");

        Directory.CreateDirectory(directory);
        var name = FileNameTemplate.Sanitize(pool.DisplayName.Replace('/', '_').Replace('\\', '_'), $"pool-{pool.Id}");
        var target = FileNameTemplate.UniqueTarget(directory, $"{name}.{format.Extension}");
        await Task.Run(() => ArchiveWriter.Write(format, entries, System.Text.Encoding.UTF8.GetBytes(xml), target,
            new Progress<double>(done => progress.Report(FetchShare + (1 - FetchShare) * done))), ct);
        progress.Report(1.0);
        return target;
    }

    private static bool IsPage(Post post) => post.FileUrl is not null && PageTypes.Contains(ExtensionOf(post));

    private static string ExtensionOf(Post post)
    {
        var dot = post.Image?.LastIndexOf('.') ?? -1;
        return dot < 0 ? "" : post.Image![(dot + 1)..].ToLowerInvariant();
    }
}
