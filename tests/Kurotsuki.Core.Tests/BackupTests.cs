using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Kurotsuki.Core.Backup;
using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Config;
using Kurotsuki.Core.Logging;
using Kurotsuki.Core.Storage;

namespace Kurotsuki.Core.Tests;

public class BackupTests
{
    private static LibraryStore Store() => new(Path.Combine(Path.GetTempPath(), "kt-bk-" + Guid.NewGuid().ToString("N") + ".db"));

    private static string Sample() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "backup-v1-sample.json"));

    [Fact]
    public void ImportsTheSampleFileSharedWithTheJavaAppAndMobile()
    {
        using var store = Store();
        var config = new UserConfiguration();
        var summary = BackupFile.Import(Sample(), config, store);

        Assert.True(summary.Boorus + summary.FavoriteTags + summary.FavoritePosts + summary.FolderPosts + summary.Reading > 0);
        Assert.NotEmpty(config.BooruConfigurations);
        Assert.NotEmpty(store.FavoriteTags());
        // importing again changes nothing (merge only)
        var again = BackupFile.Import(Sample(), config, store);
        Assert.Equal(0, again.Boorus + again.FavoriteTags + again.FavoritePosts + again.FolderPosts + again.Reading + again.Blacklist);
    }

    [Fact]
    public void ExportThenImportIntoAnEmptyLibraryReproducesIt()
    {
        using var source = Store();
        var config = new UserConfiguration
        {
            BooruConfigurations =
            [
                new() { Template = BooruTemplate.Danbooru, UserId = "me", ApiKey = "SECRET" },
                new() { Engine = BooruEngine.MoebooruShimmie, Flavor = BooruFlavor.Shimmie, CustomBaseUrl = "https://s.example/" },
            ],
            BlacklistedTags = ["gore", "blue_hair"],
            Language = "pt", PoolArchiveFormat = "cbz", ReaderRightToLeft = true, DownloadTemplate = "%id%.%ext%",
        };
        var post = new Post
        {
            Id = 9, Score = 2, Width = 3, Height = 4, Image = "a.png", Rating = "general", Tags = "a b", BooruBaseUrl = "https://danbooru.donmai.us",
            FileUrl = "https://danbooru.donmai.us/a.png", TagCategories = { ["a"] = TagCategory.Artist },
        };
        source.AddFavoriteTag("cat");
        source.CreateTagGroup("G");
        source.AddTagToGroup("G", "cat");
        source.SetFavoritePost(post, true);
        source.CreateFolder("F");
        source.AddToFolder("F", post);
        source.SaveReading(new ReadingRecord("https://e621.net#pool:3", "https://e621.net", 3, "P", "series", 4, 9, true, 5000));

        var withoutSecrets = BackupFile.Export(config, source, includeSecrets: false);
        Assert.DoesNotContain("SECRET", withoutSecrets);
        Assert.Contains("\"includesSecrets\": false", withoutSecrets);
        var json = BackupFile.Export(config, source, includeSecrets: true);
        Assert.Contains("SECRET", json);

        using var target = Store();
        var imported = new UserConfiguration();
        var summary = BackupFile.Import(json, imported, target);
        Assert.Equal(2, summary.Boorus);
        Assert.Equal(["gore", "blue_hair"], imported.BlacklistedTags);
        Assert.Equal("SECRET", imported.BooruConfigurations[0].ApiKey);
        Assert.Equal(BooruTemplate.Danbooru, imported.BooruConfigurations[0].Template);
        Assert.Equal(BooruFlavor.Shimmie, imported.BooruConfigurations[1].EffectiveFlavor);
        Assert.Equal("https://s.example", imported.BooruConfigurations[1].BaseUrl); // trailing slash normalised
        Assert.Equal("pt", imported.Language);
        Assert.True(imported.ReaderRightToLeft);
        Assert.Equal(["cat"], target.TagGroups()["G"]);
        Assert.Equal(TagCategory.Artist, target.FavoritePosts().Single().CategoryOf("a"));
        Assert.Single(target.Folders()["F"]);
        Assert.Equal(4, target.ReadingRecords().Single().Page);
    }

    [Fact]
    public void ImportMergesWithoutLosingWhatTheUserHas()
    {
        using var store = Store();
        store.AddFavoriteTag("mine");
        store.SaveReading(new ReadingRecord("k#pool:1", "https://k", 1, "New", null, 9, 10, false, 9000));
        var config = new UserConfiguration { BlacklistedTags = ["keep"], BooruConfigurations = [new() { Template = BooruTemplate.Danbooru, UserId = "old", ApiKey = "oldkey" }] };

        var json = """
            {"format":"kurotsuki-backup","version":1,
             "boorus":[{"engine":"danbooru","baseUrl":"https://danbooru.donmai.us/","login":"new"},{"engine":"nope","baseUrl":"https://x"},{"engine":"e621","baseUrl":"ftp://bad"}],
             "blacklist":["Keep","Other Tag"],"favoriteTags":["theirs"],
             "reading":[{"poolKey":"k#pool:1","booruBaseUrl":"https://k","poolId":1,"name":"Old","page":2,"total":10,"finished":false,"updatedAt":100},
                        {"poolKey":"bad","booruBaseUrl":"javascript:x","poolId":1,"page":0,"total":1}],
             "favoritePosts":[{"booruBaseUrl":"https://k","id":-5}]}
            """;
        var summary = BackupFile.Import(json, config, store);

        Assert.Equal(["mine", "theirs"], store.FavoriteTags());
        Assert.Equal(["keep", "other_tag"], config.BlacklistedTags);
        var danbooru = Assert.Single(config.BooruConfigurations);
        Assert.Equal("new", danbooru.UserId); // a non-empty login replaces the stored one
        Assert.Equal("oldkey", danbooru.ApiKey); // a missing apiKey keeps the stored one
        Assert.Equal(9, store.ReadingRecords().Single().Page); // the newer record wins
        Assert.Equal(4, summary.Skipped); // unknown engine, bad URL, bad reading entry, bad post
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"format":"other","version":1}""")]
    [InlineData("""{"format":"kurotsuki-backup","version":2}""")]
    [InlineData("""{"format":"kurotsuki-backup"}""")]
    public void RefusesFilesThatAreNotUsableBackups(string json)
    {
        using var store = Store();
        Assert.Throws<BackupFile.BackupException>(() => BackupFile.Import(json, new UserConfiguration(), store));
    }

    // ---- transfer link -------------------------------------------------------------------------------------

    [Fact]
    public void TransferLinksRoundTripAndRefusePublicAddresses()
    {
        var token = new string('a', 32);
        var link = new TransferLink(["192.168.1.5", "10.0.0.3"], 47835, token);
        Assert.Equal($"kurotsuki://transfer?h=192.168.1.5,10.0.0.3&p=47835&t={token}", link.ToUri());
        var parsed = TransferLink.Parse(link.ToUri())!;
        Assert.Equal(link.Hosts, parsed.Hosts);
        Assert.Equal((link.Port, link.Token), (parsed.Port, parsed.Token));
        Assert.Equal($"http://192.168.1.5:47835/backup?t={token}", link.UrlFor("192.168.1.5"));

        Assert.Null(TransferLink.Parse($"kurotsuki://transfer?h=8.8.8.8&p=1&t={token}")); // public
        Assert.Null(TransferLink.Parse($"kurotsuki://transfer?h=192.168.1.5,8.8.8.8&p=1&t={token}"));
        Assert.Null(TransferLink.Parse("kurotsuki://transfer?h=192.168.1.5&p=1&t=short"));
        Assert.Null(TransferLink.Parse($"kurotsuki://transfer?h=192.168.1.5&p=70000&t={token}"));
        Assert.Null(TransferLink.Parse($"http://transfer?h=192.168.1.5&p=1&t={token}"));
        Assert.True(TransferLink.IsPrivateAddress("172.31.0.1"));
        Assert.False(TransferLink.IsPrivateAddress("172.32.0.1"));
        Assert.False(TransferLink.IsPrivateAddress("300.1.1.1"));
    }

    [Fact]
    public async Task TransferServerServesTheBackupOnceToTheRightToken()
    {
        var states = new List<TransferServer.State>();
        using var server = new TransferServer(() => "{\"hello\":1}", TimeSpan.FromMinutes(1), states.Add);
        using var http = new HttpClient();
        var url = $"http://127.0.0.1:{server.Port}/backup?t=";

        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync(url + "wrong")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync($"http://127.0.0.1:{server.Port}/other?t={server.Token}")).StatusCode);
        Assert.Equal(TransferServer.State.Waiting, server.Current);

        var ok = await http.GetAsync(url + server.Token);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("{\"hello\":1}", await ok.Content.ReadAsStringAsync());
        Assert.Equal("application/json", ok.Content.Headers.ContentType!.MediaType);
        for (var i = 0; i < 100 && server.Current == TransferServer.State.Waiting; i++) await Task.Delay(10);
        Assert.Equal(TransferServer.State.Sent, server.Current);
        Assert.Equal([TransferServer.State.Sent], states);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => http.GetAsync(url + server.Token)); // it stopped listening
    }

    [Fact]
    public async Task TransferServerStopsAfterTooManyWrongTokensAndWhenTimeIsUp()
    {
        using (var server = new TransferServer(() => "x", TimeSpan.FromMinutes(1), _ => { }))
        {
            using var http = new HttpClient();
            for (var i = 0; i < 5; i++)
            {
                await http.GetAsync($"http://127.0.0.1:{server.Port}/backup?t=bad{i}");
            }
            for (var i = 0; i < 100 && server.Current == TransferServer.State.Waiting; i++) await Task.Delay(10);
            Assert.Equal(TransferServer.State.Stopped, server.Current);
        }
        using var expiring = new TransferServer(() => "x", TimeSpan.FromMilliseconds(150), _ => { });
        for (var i = 0; i < 100 && expiring.Current == TransferServer.State.Waiting; i++) await Task.Delay(20);
        Assert.Equal(TransferServer.State.Expired, expiring.Current);
        Assert.Matches("^[0-9a-f]{32}$", expiring.Token);
    }

    // ---- logging ------------------------------------------------------------------------------------------

    [Fact]
    public void LogsRotateAndNeverContainQueryStringsAndArchiveIncludesSystemInfo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kt-log-" + Guid.NewGuid().ToString("N"));
        AppLog.Reset();
        AppLog.Install(dir);
        AppLog.Info("hello " + AppLog.SafeUrl("https://gelbooru.com/index.php?api_key=SECRET&tags=x"));
        AppLog.Error("boom", new InvalidOperationException("bad"));
        var current = File.ReadAllText(Path.Combine(dir, "kurotsuki-0.log"));
        Assert.Contains("hello gelbooru.com/index.php", current);
        Assert.DoesNotContain("SECRET", current);
        Assert.Contains("ERROR", current);

        // rotation: grow the current file past the limit
        File.AppendAllText(Path.Combine(dir, "kurotsuki-0.log"), new string('x', AppLog_FileBytes) + "\n");
        AppLog.Info("after rotation");
        Assert.True(File.Exists(Path.Combine(dir, "kurotsuki-1.log")));
        Assert.Contains("after rotation", File.ReadAllText(Path.Combine(dir, "kurotsuki-0.log")));

        var tar = LogArchive.Write(dir, Path.Combine(dir, LogArchive.FileName), SystemInfo.Describe([new BooruConfiguration { Template = BooruTemplate.Gelbooru, ApiKey = "SECRET" }]));
        using var stream = File.OpenRead(tar);
        using var reader = new System.Formats.Tar.TarReader(stream);
        var names = new List<string>();
        string? info = null;
        while (reader.GetNextEntry() is { } entry)
        {
            names.Add(entry.Name);
            if (entry.Name == "system-info.txt")
            {
                info = new StreamReader(entry.DataStream!).ReadToEnd();
            }
        }
        Assert.Equal("system-info.txt", names[0]);
        Assert.Contains("logs/kurotsuki-0.log", names);
        Assert.Contains("gelbooru.com", info);
        Assert.DoesNotContain("SECRET", info);
        AppLog.Reset();
    }

    private const int AppLog_FileBytes = 1_000_000;
}

public class QrCodeTests
{
    /// <summary>Minimal grayscale PNG writer (stored zlib via ZLibStream) so a real decoder can read our matrix.</summary>
    internal static byte[] Png(bool[,] modules, int scale = 8, int quiet = 4)
    {
        var n = modules.GetLength(0);
        var size = (n + quiet * 2) * scale;
        var raw = new MemoryStream();
        for (var y = 0; y < size; y++)
        {
            raw.WriteByte(0);
            for (var x = 0; x < size; x++)
            {
                int mx = x / scale - quiet, my = y / scale - quiet;
                var dark = mx >= 0 && my >= 0 && mx < n && my < n && modules[my, mx];
                raw.WriteByte(dark ? (byte)0 : (byte)255);
            }
        }
        var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(raw.ToArray());
        }
        var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        void Chunk(string type, byte[] data)
        {
            var len = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
            png.Write(len);
            var body = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
            png.Write(body);
            var crc = 0xFFFFFFFFu;
            foreach (var b in body)
            {
                crc ^= b;
                for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
            var c = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(c, ~crc);
            png.Write(c);
        }
        var header = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, size);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), size);
        header[8] = 8; // bit depth, grayscale
        Chunk("IHDR", header);
        Chunk("IDAT", compressed.ToArray());
        Chunk("IEND", []);
        return png.ToArray();
    }

    private static string? Decode(string text)
    {
        var zbar = ExternalTools.Find("zbarimg");
        if (zbar is null)
        {
            return null;
        }
        var file = Path.Combine(Path.GetTempPath(), "kt-qr-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(file, Png(QrCode.Encode(text)));
        var result = ExternalTools.Run(zbar, "--quiet", "--raw", "-Sdisable", "-Sqrcode.enable", file);
        return result.ExitCode == 0 ? result.Output.TrimEnd('\n') : $"ERROR {result.ExitCode}: {result.Output}";
    }

    [Theory]
    [InlineData("hi")] // version 1
    [InlineData("kurotsuki://transfer?h=192.168.100.20,10.0.0.3&p=47835&t=0123456789abcdef0123456789abcdef")] // the real link
    [InlineData("A somewhat longer text with punctuation: ünïcödé ✓ and numbers 0123456789 0123456789 — fits version 5 or so")]
    public void QrCodesAreReadBackByARealDecoder(string text)
    {
        var decoded = Decode(text);
        if (decoded is null)
        {
            return; // zbarimg not installed
        }
        Assert.Equal(text, decoded);
    }

    [Fact]
    public void QrCodesDecodeAcrossVersionsOneToTen()
    {
        var random = new Random(7);
        foreach (var length in new[] { 1, 14, 26, 32, 53, 78, 106, 107, 122, 123, 134, 152, 153, 180, 181, 213 })
        {
            var text = new string(Enumerable.Range(0, length).Select(_ => (char)random.Next('a', 'z' + 1)).ToArray());
            var decoded = Decode(text);
            if (decoded is null)
            {
                return;
            }
            Assert.True(text == decoded, $"length {length}: got '{decoded}'");
        }
    }

    [Fact]
    public void QrCodeStructureIsSane()
    {
        var m = QrCode.Encode("hi");
        Assert.Equal(21, m.GetLength(0)); // version 1
        Assert.True(m[0, 0] && m[0, 6] && m[6, 0] && m[6, 6] && !m[1, 1] && m[2, 2]); // finder pattern
        Assert.True(m[21 - 8, 8]); // the always-dark module
        Assert.Equal(25, QrCode.Encode(new string('x', 20)).GetLength(0)); // version 2
        Assert.Throws<ArgumentException>(() => QrCode.Encode(new string('x', QrCode.MaxBytes + 1)));
    }
}
