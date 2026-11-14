using System.IO.Compression;
using System.Net;
using System.Text;
using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Download;
using Kurotsuki.Core.Download.Comic;
using Kurotsuki.Core.Net;

namespace Kurotsuki.Core.Tests;

public class PoolTests
{
    private sealed class Stub(Func<Uri, string> reply) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply(request.RequestUri!)) });
        }
    }

    private const string E621Pool = """{"id":4,"name":"My_pool","description":"d","category":"series","post_count":3,"post_ids":[30,10,20],"created_at":"2026-01-02T03:04:05.000Z","updated_at":null}""";

    private static string E621Post(int id) =>
        $$"""{"id":{{id}},"file":{"width":1,"height":2,"md5":"{{id:x32}}","ext":"png","url":"https://static.e621.net/{{id}}.png"},"preview":{"url":"https://static.e621.net/p{{id}}.png"},"sample":{"url":null},"score":{"total":1},"rating":"s","sources":[],"tags":{"general":["x"]},"pools":[4]}""";

    [Fact]
    public async Task E621SearchesPoolsAndOrdersPostsLikeThePool()
    {
        var stub = new Stub(uri => uri.AbsolutePath switch
        {
            "/pools.json" => $"[{E621Pool}]",
            "/pools/4.json" => E621Pool,
            // the post search returns them in the wrong order, and one is missing (not visible to the account)
            "/posts.json" => $$"""{"posts":[{{E621Post(10)}},{{E621Post(30)}}]}""",
            _ => "{}",
        });
        var client = new E621Client(new HttpClient(stub), "https://e621.net", null, null);
        Assert.True(client.SupportsPools);

        var pools = await client.SearchPoolsAsync("my pool", Pool.Series, PoolOrder.Name, 0, 40);
        var pool = Assert.Single(pools);
        Assert.Equal("My pool", pool.DisplayName);
        Assert.Equal("https://e621.net#pool:4", pool.Key);
        var listUrl = stub.Urls.First(u => u.Contains("pools.json"));
        Assert.Contains("search%5Bname_matches%5D=%2Amy_pool%2A", listUrl);
        Assert.Contains("search%5Border%5D=name", listUrl);
        Assert.Contains("page=1", listUrl);

        var posts = await client.PoolPostsAsync(pool);
        Assert.Equal([30L, 10L], posts.Select(p => p.Id));

        var covers = await client.PoolCoversAsync(pools);
        Assert.Contains("id%3A30", stub.Urls.Last()); // first post of the pool, one batched request
        Assert.Equal(30, covers[4].Id);
    }

    [Fact]
    public async Task DanbooruFindsPoolsOfAPostThroughThePoolSearch()
    {
        var stub = new Stub(_ => $"[{E621Pool}]");
        var client = new DanbooruClient(new HttpClient(stub), "https://danbooru.donmai.us", "me", "k");
        var pools = await client.PoolsOfAsync(new Post { Id = 20 });
        Assert.Single(pools);
        Assert.Empty(await client.PoolsOfAsync(new Post { Id = 99 })); // the pool does not contain it
        Assert.Contains("search%5Bpost_tags_match%5D=id%3A20", stub.Urls[0]);
    }

    [Fact]
    public async Task MoebooruPoolsComeFromShowJsonAndNeedNoPaging()
    {
        var stub = new Stub(uri => uri.AbsolutePath switch
        {
            "/pool/show.json" => """{"id":3,"name":"Konachan pool","description":"x","post_count":2,"created_at":"2025-05-05T00:00:00Z","posts":[{"id":8,"md5":"aa","file_ext":"jpg","file_url":"https://k/8.jpg","preview_url":"https://k/p8.jpg","tags":"t","rating":"s","width":1,"height":1},{"id":7,"md5":"bb","file_ext":"jpg","file_url":"https://k/7.jpg","preview_url":"https://k/p7.jpg","tags":"t","rating":"s","width":1,"height":1}]}""",
            "/post/show/55" => "<div id=\"pool3\"></div><div id=\"pool3\"></div><div id=\"pool12\"></div>",
            _ => "[]",
        });
        var client = new MoebooruClient(new HttpClient(stub), "https://konachan.com");
        Assert.False(client.SupportsPoolFilters);
        Assert.Equal(1, client.PoolPageSize(40));

        var pool = await client.GetPoolAsync(3);
        Assert.Equal("Konachan pool", pool!.Name);
        var posts = await client.PoolPostsAsync(pool);
        Assert.Equal([8L, 7L], posts.Select(p => p.Id)); // the server's order is the reading order
        Assert.Equal([3L, 12L], MoebooruClient.PoolIdsInPage("<div id=\"pool3\"></div><div id=\"pool3\"></div><div id=\"pool12\"></div>"));
    }

    [Fact]
    public async Task GelbooruTagCategoriesReadTypeNumbers()
    {
        var stub = new Stub(_ => """{"tag":[{"name":"bob","type":1},{"name":"bobby","type":4}]}""");
        var client = new GelbooruFamilyClient(new HttpClient(stub), "https://gelbooru.com", null, null);
        var categories = await client.TagCategoriesAsync(["bob"]);
        Assert.Equal(TagCategory.Artist, categories["bob"]);
    }

    // ---- archive ------------------------------------------------------------------------------------------

    private static (string Dir, List<ArchiveWriter.Entry> Entries) Pages(int count)
    {
        var dir = Path.Combine(Path.GetTempPath(), "kt-arc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var entries = new List<ArchiveWriter.Entry>();
        for (var i = 0; i < count; i++)
        {
            var file = Path.Combine(dir, $"p{i}");
            File.WriteAllBytes(file, Enumerable.Range(0, 300 + i).Select(b => (byte)(b + i)).ToArray());
            entries.Add(new ArchiveWriter.Entry($"{i + 1:000}.png", file));
        }
        return (dir, entries);
    }

    [Fact]
    public void ZipArchiveHoldsComicInfoFirstThenThePages()
    {
        var (dir, entries) = Pages(3);
        var target = Path.Combine(dir, "out.cbz");
        ArchiveWriter.Write(ArchiveFormat.Cbz, entries, Encoding.UTF8.GetBytes("<x/>"), target);

        using var zip = ZipFile.OpenRead(target);
        Assert.Equal(["ComicInfo.xml", "001.png", "002.png", "003.png"], zip.Entries.Select(e => e.FullName));
        using var page = zip.GetEntry("002.png")!.Open();
        using var copy = new MemoryStream();
        page.CopyTo(copy);
        Assert.Equal(File.ReadAllBytes(entries[1].File), copy.ToArray());
        Assert.False(File.Exists(target + ".part"));
    }

    [Fact]
    public void TarArchiveCanBeReadBack()
    {
        var (dir, entries) = Pages(2);
        var target = Path.Combine(dir, "out.cbt");
        ArchiveWriter.Write(ArchiveFormat.Parse("cbt")!, entries, Encoding.UTF8.GetBytes("<x/>"), target);

        using var stream = File.OpenRead(target);
        using var reader = new System.Formats.Tar.TarReader(stream);
        var names = new List<string>();
        while (reader.GetNextEntry() is { } entry)
        {
            names.Add(entry.Name);
        }
        Assert.Equal(["ComicInfo.xml", "001.png", "002.png"], names);
    }

    [Fact]
    public void RarArchiveStoresEveryFileWithValidHeaders()
    {
        var (dir, entries) = Pages(2);
        var target = Path.Combine(dir, "out.cbr");
        ArchiveWriter.Write(ArchiveFormat.Parse("cbr")!, entries, Encoding.UTF8.GetBytes("<x/>"), target);

        var bytes = File.ReadAllBytes(target);
        Assert.Equal([0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00], bytes[..8]);
        // Walk the headers: each is CRC32, vint size, body; file headers are followed by their data area.
        var pos = 8;
        var types = new List<int>();
        while (pos < bytes.Length)
        {
            var crc = BitConverter.ToUInt32(bytes, pos);
            pos += 4;
            var (size, n) = ReadVInt(bytes, pos);
            var bodyStart = pos + n;
            Assert.Equal(crc, Crc32(bytes[pos..(bodyStart + (int)size)]));
            var (type, _) = ReadVInt(bytes, bodyStart);
            types.Add((int)type);
            var (flags, f) = ReadVInt(bytes, bodyStart + 1);
            pos = bodyStart + (int)size;
            if ((flags & 0x2) != 0)
            {
                var (dataSize, _) = ReadVInt(bytes, bodyStart + 1 + f);
                pos += (int)dataSize;
            }
        }
        Assert.Equal([1, 2, 2, 2, 5], types); // main, ComicInfo + 2 pages, end
        Assert.Equal(bytes.Length, pos);
    }

    private static (long Value, int Length) ReadVInt(byte[] b, int at)
    {
        long value = 0;
        var shift = 0;
        var i = at;
        while (true)
        {
            value |= (long)(b[i] & 0x7F) << shift;
            if ((b[i++] & 0x80) == 0)
            {
                return (value, i - at);
            }
            shift += 7;
        }
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }
        return ~crc;
    }

    [Fact]
    public void ArchiveFormatParsing()
    {
        Assert.Equal("cbz", ArchiveFormat.Parse(" .CBZ ")!.Extension);
        Assert.Null(ArchiveFormat.Parse("exe"));
        Assert.All(ArchiveFormat.All, f => Assert.True(f.CanWrite));
    }

    [Fact]
    public void ComicInfoFollowsSchemaOrderAndEscapesText()
    {
        var pool = new Pool(1, "Tom_&_Jerry", "<b>desc</b>", "series", 2, [1, 2], "https://e621.net", "2026-03-04T00:00:00Z", null);
        var p1 = new Post { Id = 1, Width = 10, Height = 20, Rating = "explicit", Tags = "bob_(artist) manga english_text monochrome", TagCategories = { ["bob_(artist)"] = TagCategory.Artist } };
        var p2 = new Post { Id = 2, Rating = "safe", Tags = "manga monochrome", };
        var xml = ComicInfo.Xml(pool, [new(p1, 100), new(p2, 200)], "https://e621.net/pools/1", "e621.net", "Kurotsuki-Next");

        Assert.Contains("<Title>Tom &amp; Jerry</Title>", xml);
        Assert.Contains("<Summary>&lt;b&gt;desc&lt;/b&gt;</Summary>", xml);
        Assert.Contains("<Year>2026</Year>", xml);
        Assert.Contains("<Writer>bob</Writer>", xml);
        Assert.Contains("<AgeRating>Adults Only 18+</AgeRating>", xml);
        Assert.Contains("<Manga>Yes</Manga>", xml);
        Assert.Contains("<BlackAndWhite>Yes</BlackAndWhite>", xml);
        Assert.Contains("<LanguageISO>en</LanguageISO>", xml);
        Assert.Contains("<Page Image=\"0\" Type=\"FrontCover\" ImageSize=\"100\" ImageWidth=\"10\" ImageHeight=\"20\" Key=\"1\" />", xml);
        Assert.True(xml.IndexOf("<Title>") < xml.IndexOf("<Publisher>") && xml.IndexOf("<Publisher>") < xml.IndexOf("<PageCount>")
            && xml.IndexOf("<PageCount>") < xml.IndexOf("<Pages>"));
        System.Xml.Linq.XDocument.Parse(xml); // well-formed
        Assert.Equal("ab", ComicInfo.Escape("a\u0001b")); // characters XML 1.0 cannot hold are dropped
    }

    private sealed class FakeDownloader : IMediaDownloader
    {
        public List<string> Urls { get; } = [];

        public Task<string> DownloadAsync(string url, string target, IProgress<double>? progress = null, bool spaced = false, CancellationToken ct = default)
        {
            lock (Urls)
            {
                Urls.Add(url);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, [0x89, 0x50, 0x4E, 0x47, 1, 2, 3]);
            progress?.Report(1.0);
            return Task.FromResult(target);
        }
    }

    private sealed class FakeClient(List<Post> posts) : IBooruClient
    {
        public bool SupportsPools => true;
        public Task<IReadOnlyList<Post>> SearchPostsAsync(string? tags, int page, int limit, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Post>>([]);
        public Task<IReadOnlyList<string>> SuggestTagsAsync(string prefix, int limit, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<Post>> PoolPostsAsync(Pool pool, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Post>>(posts);
    }

    [Fact]
    public async Task PoolArchiverBuildsACbzWithPagesInPoolOrder()
    {
        var posts = Enumerable.Range(1, 4).Select(i => new Post
        {
            Id = i, Image = i == 3 ? "clip.webm" : $"{i}.png", FileUrl = $"https://x/{i}", Width = 1, Height = 1,
        }).ToList();
        var downloader = new FakeDownloader();
        var cacheDir = Path.Combine(Path.GetTempPath(), "kt-cache-" + Guid.NewGuid().ToString("N"));
        var cache = new ImageCache(() => null, cacheDir, downloader);
        var pool = new Pool(1, "Comic/One", null, "series", 4, [1, 2, 3, 4], "https://x", null, null);
        var archiver = new PoolArchiver(() => "cbz", _ => new PoolArchiver.Source(new FakeClient(posts), "https://x", "x"),
            (url, ct) => cache.FetchAsync(url, ct));

        var outDir = Path.Combine(cacheDir, "out");
        var result = await archiver.ArchiveAsync(pool, outDir, new Progress<double>());

        Assert.EndsWith("Comic_One.cbz", result);
        using var zip = ZipFile.OpenRead(result);
        Assert.Equal(["ComicInfo.xml", "001.png", "002.png", "003.png"], zip.Entries.Select(e => e.FullName)); // the webm is not a page
        Assert.Equal(3, downloader.Urls.Count);
    }

    [Fact]
    public void ArchiverRejectsUnknownAndUnwritableFormats()
    {
        PoolArchiver Make(string? fmt) => new(() => fmt, _ => null, (_, _) => Task.FromResult(""));
        Assert.Equal("cbz", Make(null).ConfiguredFormat().Extension);
        Assert.Throws<IOException>(() => Make("exe").ConfiguredFormat());
        Assert.Equal("7z", Make("7z").ConfiguredFormat().Extension);
    }
}

public class IndependentArchiveCheckTests
{
    private static (string Dir, List<ArchiveWriter.Entry> Entries) Pages(int count)
    {
        var dir = Path.Combine(Path.GetTempPath(), "kt-ext-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var entries = new List<ArchiveWriter.Entry>();
        for (var i = 0; i < count; i++)
        {
            var file = Path.Combine(dir, $"src{i}");
            File.WriteAllBytes(file, Enumerable.Range(0, 5000 + i * 777).Select(b => (byte)(b * 7 + i)).ToArray());
            entries.Add(new ArchiveWriter.Entry($"{i + 1:000}.png", file));
        }
        return (dir, entries);
    }

    [Fact]
    public void SevenZipOpensInRealSevenZipAndExtractsIdenticalFiles()
    {
        var (dir, entries) = Pages(4);
        var target = Path.Combine(dir, "out.cb7");
        ArchiveWriter.Write(ArchiveFormat.Parse("cb7")!, entries, Encoding.UTF8.GetBytes("<ComicInfo/>"), target);
        Assert.StartsWith("7z¼¯'\u001C", Encoding.Latin1.GetString(File.ReadAllBytes(target)[..6]));

        var sevenZip = ExternalTools.Find("7z") ?? ExternalTools.Find("7za") ?? ExternalTools.Find("7zr");
        if (sevenZip is null)
        {
            return; // not installed: header check above is all we can do
        }
        var test = ExternalTools.Run(sevenZip, "t", target);
        Assert.True(test.ExitCode == 0, test.Output);
        Assert.Contains("Everything is Ok", test.Output);

        var extracted = Path.Combine(dir, "x");
        var extract = ExternalTools.Run(sevenZip, "x", "-y", "-o" + extracted, target);
        Assert.True(extract.ExitCode == 0, extract.Output);
        Assert.Equal("<ComicInfo/>", File.ReadAllText(Path.Combine(extracted, "ComicInfo.xml")));
        foreach (var entry in entries)
        {
            Assert.Equal(File.ReadAllBytes(entry.File), File.ReadAllBytes(Path.Combine(extracted, entry.Name)));
        }
    }

    [Fact]
    public void SevenZipNumbersUseTheVariableLengthEncoding()
    {
        Assert.Equal([0x05], SevenZipWriter.EncodeNumber(5));
        Assert.Equal([0x80, 0x80], SevenZipWriter.EncodeNumber(128));
        Assert.Equal([0xC0, 0x00, 0x40], SevenZipWriter.EncodeNumber(0x4000));
        Assert.Equal([0xC1, 0x02, 0x03], SevenZipWriter.EncodeNumber(0x01_0302)); // two extra bytes: top bits go in the first byte
    }

    [Fact]
    public void RarOpensInRealUnrarAndExtractsIdenticalFiles()
    {
        var (dir, entries) = Pages(3);
        var target = Path.Combine(dir, "out.cbr");
        ArchiveWriter.Write(ArchiveFormat.Parse("cbr")!, entries, Encoding.UTF8.GetBytes("<ComicInfo/>"), target);

        var unrar = ExternalTools.Find("unrar");
        if (unrar is null)
        {
            return;
        }
        var test = ExternalTools.Run(unrar, "t", target);
        Assert.True(test.ExitCode == 0, test.Output);
        var extracted = Path.Combine(dir, "x");
        Directory.CreateDirectory(extracted);
        var extract = ExternalTools.Run(unrar, "x", "-y", target, extracted + "/");
        Assert.True(extract.ExitCode == 0, extract.Output);
        foreach (var entry in entries)
        {
            Assert.Equal(File.ReadAllBytes(entry.File), File.ReadAllBytes(Path.Combine(extracted, entry.Name)));
        }
    }
}

public class PoolSeriesTests
{
    private static Pool P(long id, string name) => new(id, name, null, "series", 10, [], "https://x", null, null);

    [Theory]
    [InlineData("Foo Bar Vol. 3", "Foo Bar", 3)]
    [InlineData("foo_bar_2", "foo bar", 2)]
    [InlineData("Foo Bar (12)", "Foo Bar", 12)]
    [InlineData("Foo Bar #4", "Foo Bar", 4)]
    [InlineData("Foo Bar, Part 5", "Foo Bar", 5)]
    [InlineData("Foo Bar 07", "Foo Bar", 7)]
    public void ParsesTheTrailingNumber(string name, string baseName, int number) =>
        Assert.Equal((baseName, number), PoolSeries.Parse(name));

    [Theory]
    [InlineData("Foo Bar")]
    [InlineData("7")]
    [InlineData("A 3")]
    [InlineData(null)]
    public void NamesWithoutABaseAndNumberAreNotSeries(string? name) => Assert.Null(PoolSeries.Parse(name));

    [Fact]
    public void NextIsTheSmallestHigherNumberOfTheSameBase()
    {
        var current = P(1, "Foo Bar vol 2");
        var next = PoolSeries.Next(current,
        [
            P(1, "Foo Bar vol 2"), P(2, "Foo Bar vol 5"), P(3, "foo bar vol 3"), P(4, "Foo Bar vol 1"), P(5, "Other Thing vol 3"),
        ]);
        Assert.Equal(3, next?.Id);
        Assert.Null(PoolSeries.Next(P(9, "Foo Bar vol 5"), [P(2, "Foo Bar vol 5"), P(4, "Foo Bar vol 1")]));
        Assert.Null(PoolSeries.Next(P(9, "Standalone"), [P(2, "Standalone 2")]));
    }
}
