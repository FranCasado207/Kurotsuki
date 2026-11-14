using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Download;
using Kurotsuki.Core.I18n;
using Kurotsuki.Core.Net;
using Kurotsuki.Core.Storage;

namespace Kurotsuki.Core.Tests;

public class QueueAndMiscTests
{
    private sealed class Fake : IMediaDownloader
    {
        public List<string> Targets { get; } = [];
        public bool Fail { get; set; }

        public Task<string> DownloadAsync(string url, string target, IProgress<double>? progress = null, bool spaced = false, CancellationToken ct = default)
        {
            if (Fail)
            {
                throw new IOException("HTTP 500");
            }
            Targets.Add(target);
            File.WriteAllText(target, "data");
            return Task.FromResult(target);
        }
    }

    private static Post P(long id) => new()
    {
        Id = id, Image = $"{id}.jpg", FileUrl = $"https://h.example/{id}.jpg", BooruBaseUrl = "https://h.example", Tags = "a",
        TagCategories = { ["a"] = TagCategory.Artist },
    };

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 400 && !condition(); i++)
        {
            await Task.Delay(10);
        }
        Assert.True(condition());
    }

    private static string Dir() => Path.Combine(Path.GetTempPath(), "kt-q-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task QueueDownloadsSkipsKnownAndPersistsAcrossRestarts()
    {
        var folder = Dir();
        var db = Dir() + ".db";
        var fake = new Fake();
        var errors = new List<string>();
        using (var store = new LibraryStore(db))
        {
            var queue = new DownloadQueue(fake, null, () => new DownloadSettings(folder, "%artist%/%id%.%ext%", SidecarFormat.Txt, true),
                null, store, errors.Add);
            queue.AddAll([P(1), P(2), P(1)]);
            Assert.Equal(2, queue.Items.Count); // duplicate ignored
            queue.StartAll();
            await Until(() => queue.Items.All(i => i.Status == DownloadStatus.Done));

            Assert.True(File.Exists(Path.Combine(folder, "a", "1.jpg")));
            Assert.Equal("a\n", File.ReadAllText(Path.Combine(folder, "a", "1.jpg.txt")));
            Assert.True(queue.WasDownloaded(P(1).Key));

            queue.Toggle(P(1)); // toggling a finished item removes it
            queue.Toggle(P(1)); // and queues it again
            queue.StartAll();
            Assert.Equal(DownloadStatus.Skipped, queue.Items.Single(i => i.Key == P(1).Key).Status);

            queue.RemoveFinished();
            queue.Toggle(P(9)); // left queued
        }

        using (var store = new LibraryStore(db))
        {
            var again = new DownloadQueue(fake, null, () => new DownloadSettings(folder, null, SidecarFormat.None, true), null, store, errors.Add);
            Assert.Equal([P(9).Key], again.Items.Select(i => i.Key));
            Assert.True(again.WasDownloaded(P(2).Key));
            again.ForgetHistory();
            Assert.False(again.WasDownloaded(P(2).Key));
        }
        Assert.Empty(errors);
    }

    [Fact]
    public async Task TheSameFileFromAnotherBooruIsNotDownloadedTwice()
    {
        var folder = Dir();
        var fake = new Fake();
        var index = new DuplicateIndex(Path.Combine(folder, "hashes.txt"));
        var queue = new DownloadQueue(fake, null, () => new DownloadSettings(folder, "%id%.%ext%", SidecarFormat.None, true), null, null, _ => { })
        {
            Duplicates = index,
        };
        var md5 = "00112233445566778899aabbccddeeff";
        Post At(string booru, long id) => new()
        {
            Id = id, BooruBaseUrl = booru, FileUrl = booru + "/f.jpg", Image = md5 + ".jpg", Tags = "x",
        };

        var first = At("https://one", 1);
        queue.Toggle(first);
        queue.StartAll();
        await Until(() => queue.Items[0].Status == DownloadStatus.Done);

        var twin = At("https://two", 7);
        queue.Toggle(twin);
        queue.StartAll();
        var item = queue.Items.Single(i => i.Key == twin.Key);
        Assert.Equal(DownloadStatus.Skipped, item.Status);
        Assert.Single(fake.Targets);

        queue.ForgetHistory(); // forgetting what was downloaded forgets the hashes too
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public async Task FailedDownloadsKeepTheirMessageAndCanBeRetried()
    {
        var fake = new Fake { Fail = true };
        var queue = new DownloadQueue(fake, null, () => new DownloadSettings(Dir(), null, SidecarFormat.None, false), null, null, _ => { });
        queue.Toggle(P(5));
        queue.StartAll();
        var item = queue.Items.Single();
        await Until(() => item.Status == DownloadStatus.Failed);
        Assert.Contains("HTTP 500", item.Label);

        fake.Fail = false;
        item.Status = DownloadStatus.Queued;
        queue.Start(item);
        await Until(() => item.Status == DownloadStatus.Done);
        Assert.NotNull(item.SavedPath);
    }

    [Fact]
    public void StartingWithoutADownloadFolderReportsAnError()
    {
        var errors = new List<string>();
        var queue = new DownloadQueue(new Fake(), null, () => new DownloadSettings(null, null, SidecarFormat.None, false), null, null, errors.Add);
        queue.Toggle(P(1));
        queue.StartAll();
        Assert.Single(errors);
        Assert.Equal(DownloadStatus.Queued, queue.Items[0].Status);
    }

    [Fact]
    public async Task BulkFetchStopsAtShortPageCapAndErrors()
    {
        var throttle = new HostThrottle(TimeSpan.Zero);
        Task<IReadOnlyList<Post>> Page(int page) => Task.FromResult<IReadOnlyList<Post>>(
            page switch { 0 => [P(1), P(2)], 1 => [P(2), P(3)], _ => [P(4)] });

        var all = await BulkFetch.FetchAsync(Page, 0, 10, 2, 100, _ => true, throttle, "h");
        Assert.Equal([1L, 2L, 3L, 4L], all.Posts.Select(p => p.Id)); // de-duplicated; stops after the short page
        Assert.Equal(3, all.PagesRead);

        var capped = await BulkFetch.FetchAsync(Page, 0, 10, 2, 3, _ => true, throttle, "h");
        Assert.True(capped.Truncated);
        Assert.Equal(3, capped.Posts.Count);

        var filtered = await BulkFetch.FetchAsync(Page, 0, 10, 2, 100, p => p.Id != 2, throttle, "h");
        Assert.Equal([1L, 3L, 4L], filtered.Posts.Select(p => p.Id));

        var failing = await BulkFetch.FetchAsync(p => p == 1 ? throw new IOException("boom") : Page(p), 0, 10, 2, 100, _ => true, throttle, "h");
        Assert.Equal("boom", failing.Error);
        Assert.Equal(2, failing.Posts.Count); // what was read is kept
    }

    [Fact]
    public void PropertiesBundlesLoadWithFormattingAndFallback()
    {
        Messages.SetLanguage("en");
        Assert.Equal("Download queue (3)", Messages.Get("download.queueTitle", 3));
        Assert.Equal("Download queue ({0})", Messages.Get("download.queueTitle")); // no args: pattern as is
        Assert.Equal("no.such.key", Messages.Get("no.such.key"));
        Assert.Equal("Finished ✓", Messages.Get("reader.finished")); // \u escapes

        Messages.SetLanguage("pt-PT");
        Assert.Equal("pt", Messages.Language);
        Assert.NotEqual("Finished ✓", Messages.Get("reader.finished"));
        Assert.Contains("3", Messages.Get("download.queueTitle", 3));
        Messages.SetLanguage("xx");
        Assert.Equal("en", Messages.Language);
        Messages.SetLanguage("en");

        Assert.Equal("it's 5", Messages.Format("it''s {0}", [5]));
        Assert.Equal("{0}", Messages.Format("'{'0}", [1])); // quoted braces are literal
    }

    [Fact]
    public void BothBundlesHaveTheSameKeys()
    {
        var asm = typeof(Messages).Assembly;
        Dictionary<string, string> Load(string name)
        {
            using var s = asm.GetManifestResourceStream(asm.GetManifestResourceNames().Single(n => n.EndsWith(name)))!;
            return Messages.ParseProperties(new StreamReader(s).ReadToEnd());
        }
        var en = Load("messages.properties");
        var pt = Load("messages_pt.properties");
        Assert.Empty(en.Keys.Except(pt.Keys));
        Assert.Empty(pt.Keys.Except(en.Keys));
    }

    [Fact]
    public async Task ImageCacheSharesDownloadsTrimsAndOnlyTouchesItsOwnFiles()
    {
        var dir = Dir();
        var fake = new CountingDownloader();
        var cache = new ImageCache(() => null, dir, fake);

        var first = cache.FetchAsync("https://x/1");
        var second = cache.FetchAsync("https://x/1");
        await Task.WhenAll(first, second);
        Assert.Equal(1, fake.Calls); // concurrent requests share one download
        await cache.FetchAsync("https://x/1");
        Assert.Equal(1, fake.Calls); // and the next one is a cache hit

        File.WriteAllText(Path.Combine(dir, "notes.txt"), "mine");
        File.WriteAllText(Path.Combine(dir, ImageCache.Hash("old")), new string('x', 1000));
        Assert.Equal(1000 + 10, cache.SizeBytes());
        cache.Trim(10); // keeps at most 10 bytes: the oldest go first
        Assert.True(File.Exists(Path.Combine(dir, "notes.txt")));
        Assert.True(cache.SizeBytes() <= 10);
        Assert.True(cache.Clear() >= 0);
        Assert.True(File.Exists(Path.Combine(dir, "notes.txt")));
    }

    private sealed class CountingDownloader : IMediaDownloader
    {
        public int Calls;

        public async Task<string> DownloadAsync(string url, string target, IProgress<double>? progress = null, bool spaced = false, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            await Task.Delay(50, ct);
            File.WriteAllText(target, "0123456789");
            return target;
        }
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0, 0 }, "png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "jpg")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39 }, "gif")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, "webp")]
    [InlineData(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }, "webm")]
    [InlineData(new byte[] { 1, 2, 3 }, "bin")]
    public void SniffsFileTypesFromBytes(byte[] bytes, string expected)
    {
        var file = Path.GetTempFileName();
        File.WriteAllBytes(file, bytes);
        Assert.Equal(expected, ImageSniffer.DetectExtension(file, "bin"));
    }

    [Fact]
    public void PoolJsonMatchesTheJavaRecordAndUrlsAreBuiltPerEngine()
    {
        var pool = new Pool(3, "n", null, "series", 2, [1, 2], "https://e621.net", null, null);
        var json = System.Text.Json.JsonSerializer.Serialize(pool, Config.ConfigManager.Json);
        Assert.Contains("\"postIds\": [", json);
        Assert.Contains("\"booruBaseUrl\"", json);
        var back = System.Text.Json.JsonSerializer.Deserialize<Pool>(json, Config.ConfigManager.Json)!;
        Assert.Equal([1L, 2L], back.PostIds);

        Assert.Equal("https://e621.net/pools/3", BooruHttp.PoolUrl(BooruEngine.E621, null, "https://e621.net/", 3));
        Assert.Equal("https://k/pool/show/3", BooruHttp.PoolUrl(BooruEngine.MoebooruShimmie, BooruFlavor.Moebooru, "https://k", 3));
        Assert.Null(BooruHttp.PoolUrl(BooruEngine.GelbooruFamily, null, "https://g", 3));
    }
}
