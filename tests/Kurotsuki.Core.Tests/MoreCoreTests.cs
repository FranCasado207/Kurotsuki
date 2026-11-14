using System.IO.Compression;
using System.Net;
using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Config;
using Kurotsuki.Core.Download;
using Kurotsuki.Core.Media;
using Kurotsuki.Core.Net;

namespace Kurotsuki.Core.Tests;

public class MoreCoreTests
{
    private sealed class ZipDownloader : IMediaDownloader
    {
        public Task<string> DownloadAsync(string url, string target, IProgress<double>? progress = null, bool spaced = false, CancellationToken ct = default)
        {
            using (var zip = ZipFile.Open(target, ZipArchiveMode.Create))
            {
                for (var i = 0; i < 3; i++)
                {
                    using var s = zip.CreateEntry($"{i}.png").Open();
                    s.Write([2, 0, 2, 0, .. new byte[16].Select((_, k) => (byte)(k * 16 + i * 40))]); // 2x2 raw RGBA
                }
            }
            return Task.FromResult(target);
        }
    }

    private sealed class RawDecoder : IFrameDecoder
    {
        public DecodedImage? Decode(byte[] data) => data.Length == 20 ? new DecodedImage(2, 2, data[4..]) : null;
    }

    [Fact]
    public async Task UgoiraZipIsConvertedToGifByThePostProcessHookAndTheSidecarFollowsIt()
    {
        var folder = Path.Combine(Path.GetTempPath(), "kt-ug-" + Guid.NewGuid().ToString("N"));
        var queue = new DownloadQueue(new ZipDownloader(), null, () => new DownloadSettings(folder, null, SidecarFormat.Txt, false), null, null, _ => { })
        {
            PostProcess = (path, _) => Task.Run(() =>
            {
                if (!Ugoira.IsUgoiraZip(path))
                {
                    return path;
                }
                var gif = FileNameTemplate.UniqueTarget(Path.GetDirectoryName(path)!, Path.GetFileName(Ugoira.GifNameFor(path)));
                Ugoira.ConvertToGif(path, gif, new RawDecoder());
                File.Delete(path);
                return gif;
            }),
        };
        queue.Toggle(new Post { Id = 1, Image = "x.zip", FileUrl = "https://h/x.zip", BooruBaseUrl = "https://h", Tags = "t" });
        queue.StartAll();
        var item = queue.Items.Single();
        for (var i = 0; i < 300 && item.Status == DownloadStatus.Downloading; i++) await Task.Delay(10);

        Assert.Equal(DownloadStatus.Done, item.Status);
        Assert.EndsWith("x.gif", item.SavedPath);
        Assert.False(File.Exists(Path.Combine(folder, "x.zip")));
        Assert.Equal(3, GifDecoder.Frames(item.SavedPath!).Count());
        Assert.True(File.Exists(item.SavedPath + ".txt"));
    }

    [Fact]
    public async Task AFailingPostProcessKeepsTheDownloadedFileAndReportsIt()
    {
        var errors = new List<string>();
        var folder = Path.Combine(Path.GetTempPath(), "kt-ug-" + Guid.NewGuid().ToString("N"));
        var queue = new DownloadQueue(new ZipDownloader(), null, () => new DownloadSettings(folder, null, SidecarFormat.None, false), null, null, errors.Add)
        {
            PostProcess = (_, _) => throw new IOException("no codec"),
        };
        queue.Toggle(new Post { Id = 2, Image = "y.zip", FileUrl = "https://h/y.zip", BooruBaseUrl = "https://h" });
        queue.StartAll();
        var item = queue.Items.Single();
        for (var i = 0; i < 300 && item.Status == DownloadStatus.Downloading; i++) await Task.Delay(10);
        Assert.Equal(DownloadStatus.Done, item.Status);
        Assert.True(File.Exists(item.SavedPath));
        Assert.Contains(errors, e => e.Contains("no codec"));
    }

    [Fact]
    public void LegacyFolderIsMovedToTheNewLocationsOnce()
    {
        var root = Path.Combine(Path.GetTempPath(), "kt-mig-" + Guid.NewGuid().ToString("N"));
        var legacy = Path.Combine(root, "kurotsuki");
        Directory.CreateDirectory(Path.Combine(legacy, ".cache"));
        File.WriteAllText(Path.Combine(legacy, "config.json"), "{}");
        File.WriteAllText(Path.Combine(legacy, "kurotsuki.db"), "db");
        File.WriteAllText(Path.Combine(legacy, "kurotsuki.db-wal"), "wal");
        File.WriteAllText(Path.Combine(legacy, ".cache", "abc"), "c");
        var target = new AppPaths(Path.Combine(root, "cfg"), Path.Combine(root, "data"), Path.Combine(root, "cache"));

        AppPaths.MigrateLegacy(legacy, target);

        Assert.Equal("{}", File.ReadAllText(Path.Combine(target.Config, "config.json")));
        Assert.Equal("db", File.ReadAllText(target.DatabaseFile));
        Assert.True(File.Exists(target.DatabaseFile + "-wal"));
        Assert.True(File.Exists(Path.Combine(target.Cache, "abc")));
        Assert.False(Directory.Exists(legacy));

        // a second run, or one with a config already in place, changes nothing
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "config.json"), "other");
        AppPaths.MigrateLegacy(legacy, target);
        Assert.Equal("{}", File.ReadAllText(Path.Combine(target.Config, "config.json")));
        Assert.True(File.Exists(Path.Combine(legacy, "config.json")));
    }

    private sealed class Stub(Func<Uri, string> reply) : HttpMessageHandler
    {
        public List<(string Url, string? Key)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.RequestUri!.AbsoluteUri, request.Headers.TryGetValues("Hydrus-Client-API-Access-Key", out var v) ? v.Single() : null));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply(request.RequestUri)) });
        }
    }

    [Fact]
    public async Task HydrusAutocompleteSortsByCountAndSendsTheKeyAsAHeader()
    {
        var stub = new Stub(_ => """{"tags":[{"value":"cat","count":5},{"value":"cat ears","count":50},{"value":"category","count":9}]}""");
        var client = new HydrusClient(new HttpClient(stub), "http://127.0.0.1:45869", "KEY");
        var names = await client.SuggestTagsAsync("cat", 2);

        Assert.Equal(["cat ears", "category"], names);
        Assert.Equal("KEY", stub.Requests[0].Key);
        Assert.Contains("add_tags/search_tags?search=cat", stub.Requests[0].Url);
        Assert.DoesNotContain("KEY", stub.Requests[0].Url);
    }
}

public class SearchHistoryTests
{
    [Fact]
    public void KeepsNewestFirstWithoutRepeatsAndSurvivesRestart()
    {
        var file = Path.Combine(Path.GetTempPath(), "kt-hist-" + Guid.NewGuid().ToString("N"), "h.json");
        var history = new Kurotsuki.Core.Library.SearchHistory(file);
        history.Add("cat  ears");
        history.Add("dog");
        history.Add("  ");
        history.Add(null);
        history.Add("CAT ears");
        Assert.Equal(["CAT ears", "dog"], history.Items);

        Assert.Equal(["CAT ears", "dog"], new Kurotsuki.Core.Library.SearchHistory(file).Items);
        history.Clear();
        Assert.Empty(new Kurotsuki.Core.Library.SearchHistory(file).Items);
    }

    [Fact]
    public void IsCappedAndToleratesACorruptFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kt-hist-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "h.json");
        File.WriteAllText(file, "{ broken");
        var history = new Kurotsuki.Core.Library.SearchHistory(file);
        Assert.Empty(history.Items);
        for (var i = 0; i < 40; i++)
        {
            history.Add("tag" + i);
        }
        Assert.Equal(Kurotsuki.Core.Library.SearchHistory.Limit, history.Items.Count);
        Assert.Equal("tag39", history.Items[0]);
    }
}

public class BlacklistReportTests
{
    private static Kurotsuki.Core.Booru.Post PostWith(int id, string tags) =>
        new() { Id = id, Tags = tags, BooruBaseUrl = "https://b.example" };

    [Fact]
    public void MatchingTagsListsOnlyBlacklistedOnesNormalised()
    {
        var blocked = new HashSet<string> { "cat_ears", "gore" };
        var post = PostWith(1, "solo Cat_Ears dog gore cat_ears");
        Assert.Equal(["cat_ears", "gore"], Kurotsuki.Core.Config.TagBlacklist.MatchingTags(post, blocked));
        Assert.Empty(Kurotsuki.Core.Config.TagBlacklist.MatchingTags(PostWith(2, "dog"), blocked));
    }

    [Fact]
    public void AnAllowedPostIsNotBlockedAndTheChoiceSurvivesARestart()
    {
        var file = Path.Combine(Path.GetTempPath(), "kt-allow-" + Guid.NewGuid().ToString("N"), "a.json");
        var blocked = new HashSet<string> { "gore" };
        var post = PostWith(7, "gore");
        var other = PostWith(8, "gore");
        var allowed = new Kurotsuki.Core.Library.BlacklistExceptions(file);

        Assert.True(Kurotsuki.Core.Config.TagBlacklist.IsBlocked(post, blocked, allowed));
        allowed.Allow(post.Key);
        Assert.False(Kurotsuki.Core.Config.TagBlacklist.IsBlocked(post, blocked, allowed));
        Assert.True(Kurotsuki.Core.Config.TagBlacklist.IsBlocked(other, blocked, allowed));

        var again = new Kurotsuki.Core.Library.BlacklistExceptions(file);
        Assert.False(Kurotsuki.Core.Config.TagBlacklist.IsBlocked(post, blocked, again));
        again.Clear();
        Assert.True(Kurotsuki.Core.Config.TagBlacklist.IsBlocked(post, blocked, new Kurotsuki.Core.Library.BlacklistExceptions(file)));
    }
}
