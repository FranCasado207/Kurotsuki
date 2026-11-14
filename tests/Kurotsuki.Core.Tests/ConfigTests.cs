using System.Text.Json;
using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Config;

namespace Kurotsuki.Core.Tests;

public class ConfigTests
{
    private static (ConfigManager Manager, string Dir) Fresh()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kt-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "java-config.json"), Path.Combine(dir, "config.json"));
        return (new ConfigManager(new AppPaths(dir, dir, dir)), dir);
    }

    [Fact]
    public void ReadsTheFileTheJavaAppWrote()
    {
        var (manager, _) = Fresh();
        var config = manager.Load();

        Assert.Equal("/tmp/dl", config.DownloadFolder);
        Assert.Equal("pt", config.Language);
        Assert.Equal(300.0, config.PanelSizes["TAGS"]);
        Assert.True(config.ReaderRightToLeft);
        Assert.Equal(512, config.ReaderCacheMegabytes);
        Assert.True(config.RedownloadKnown);

        // the unknown engine is dropped; the legacy Danbooru entry is migrated
        Assert.Equal(5, config.BooruConfigurations.Count);
        var gelbooru = config.BooruConfigurations[0];
        Assert.Equal(BooruTemplate.Gelbooru, gelbooru.Template);
        Assert.Equal(BooruEngine.GelbooruFamily, gelbooru.EffectiveEngine);
        Assert.Equal("https://gelbooru.com", gelbooru.BaseUrl);
        Assert.Equal(BooruFlavor.Moebooru, config.BooruConfigurations[1].EffectiveFlavor);
        Assert.Equal(BooruFlavor.Shimmie, config.BooruConfigurations[2].EffectiveFlavor);
        Assert.Equal(BooruEngine.Szurubooru, config.BooruConfigurations[3].EffectiveEngine);
        var legacy = config.BooruConfigurations[4];
        Assert.Equal(BooruTemplate.Danbooru, legacy.Template);
        Assert.Equal("me", legacy.UserId);

        var post = Assert.Single(config.FavoritePosts);
        Assert.Equal("https://gelbooru.com#7", post.Key);
        Assert.Equal(TagCategory.Artist, post.TagCategories["a"]);
        Assert.Equal([1L, 2L], post.PoolIds);
    }

    [Fact]
    public void WritesTheSameShapeBackAndKeepsUnknownFields()
    {
        var (manager, _) = Fresh();
        manager.Save(manager.Load());

        using var doc = JsonDocument.Parse(File.ReadAllText(manager.FilePath));
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("someFutureField").GetProperty("n").GetInt32());
        var first = root.GetProperty("booruConfigurations")[0];
        Assert.Equal("GELBOORU", first.GetProperty("template").GetString());
        Assert.False(first.TryGetProperty("engine", out _));
        Assert.Equal("MOEBOORU_SHIMMIE", root.GetProperty("booruConfigurations")[2].GetProperty("engine").GetString());
        Assert.Equal("SHIMMIE", root.GetProperty("booruConfigurations")[2].GetProperty("flavor").GetString());
        var post = root.GetProperty("favoritePosts")[0];
        Assert.Equal("ARTIST", post.GetProperty("tagCategories").GetProperty("a").GetString());
        Assert.Equal("https://gelbooru.com", post.GetProperty("booruBaseUrl").GetString());
        Assert.Equal(300.0, root.GetProperty("panelSizes").GetProperty("TAGS").GetDouble());
    }

    [Fact]
    public void SavedFileIsOwnerOnlyOnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        var (manager, _) = Fresh();
        manager.Save(manager.Load());
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(manager.FilePath));
    }

    [Theory]
    [InlineData("HYDRUS", BooruTemplate.Hydrus)]
    [InlineData("E621", BooruTemplate.E621)]
    [InlineData("KONACHAN", BooruTemplate.Konachan)]
    public void EnumNamesMatchTheJavaOnes(string json, BooruTemplate expected)
    {
        var booru = JsonSerializer.Deserialize<BooruConfiguration>($"{{\"template\":\"{json}\"}}", ConfigManager.Json);
        Assert.Equal(expected, booru!.Template);
        Assert.Contains($"\"{json}\"", JsonSerializer.Serialize(booru, ConfigManager.Json));
    }

    [Fact]
    public void PathsFollowXdgOnLinuxAndHomeFolderElsewhere()
    {
        var linux = AppPaths.Resolve(false, false, v => v == "XDG_CONFIG_HOME" ? "/x/cfg" : v == "XDG_CACHE_HOME" ? "rel" : null, "/home/u");
        Assert.Equal("/x/cfg/kurotsuki", linux.Config);
        Assert.Equal("/home/u/.local/share/kurotsuki", linux.Data);
        Assert.Equal("/home/u/.cache/kurotsuki", linux.Cache); // relative XDG values are ignored

        var win = AppPaths.Resolve(true, false, _ => null, "/home/u");
        Assert.Equal("/home/u/kurotsuki", win.Config);
        Assert.Equal("/home/u/kurotsuki/.cache", win.Cache);
    }

    [Fact]
    public void BlacklistNormalisesAndMatches()
    {
        var post = new Post { Tags = "cat_ears Solo" };
        Assert.True(TagBlacklist.IsBlocked(post, new HashSet<string> { "solo" }));
        Assert.False(TagBlacklist.IsBlocked(post, new HashSet<string> { "dog" }));
        Assert.Equal("cat_ears", TagBlacklist.Normalize("  Cat Ears "));
    }
}
