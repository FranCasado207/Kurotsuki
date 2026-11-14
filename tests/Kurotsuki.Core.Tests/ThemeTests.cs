using Kurotsuki.Core.Theming;

namespace Kurotsuki.Core.Tests;

public class ThemeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kt-theme-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    [Fact]
    public void BuiltInThemesDefineEveryTokenAsValidColors()
    {
        foreach (var theme in new[] { ThemeDefinition.Dark, ThemeDefinition.Light })
        {
            Assert.All(ThemeDefinition.Tokens, t => Assert.True(ThemeDefinition.IsHexColor(theme.Colors[t]), $"{theme.Name}.{t}"));
        }
        Assert.True(ThemeDefinition.Dark.IsDark);
        Assert.False(ThemeDefinition.Light.IsDark);
    }

    [Fact]
    public void ParseKeepsValidColorsDropsBadOnesAndFallsBackToTheBase()
    {
        var theme = ThemeDefinition.Parse("""{ "name": "Mine", "base": "light", "colors": { "ACCENT": "#ff0000", "text": "red", "bogus": "#000000" } }""");
        Assert.Equal("Mine", theme.Name);
        Assert.False(theme.IsDark);
        Assert.Equal("#FF0000", theme.Color("accent"));
        Assert.Equal(ThemeDefinition.Light.Color("text"), theme.Color("text"));
        Assert.DoesNotContain("bogus", theme.Colors.Keys);
    }

    [Fact]
    public void ParseRequiresAName() => Assert.Throws<FormatException>(() => ThemeDefinition.Parse("""{ "colors": {} }"""));

    [Fact]
    public void JsonRoundTripsAndStoreListsCustomThemesAfterBuiltIns()
    {
        var store = new ThemeStore(_dir);
        var saved = store.Save(new ThemeDefinition("Ocean Night", true, new Dictionary<string, string> { ["accent"] = "#0099FF" }));
        Assert.EndsWith("ocean-night.json", saved);

        var all = store.All();
        Assert.Equal([.. ThemeDefinition.BuiltIns.Select(t => t.Name), "Ocean Night"], all.Select(t => t.Name));
        Assert.Equal("#0099FF", store.Find("ocean night").Color("accent"));

        store.Delete("Ocean Night");
        Assert.Equal(ThemeDefinition.BuiltIns.Count, store.All().Count);
    }

    [Fact]
    public void BrokenFilesAreIgnoredAndBuiltInNamesCannotBeShadowed()
    {
        var store = new ThemeStore(_dir);
        Directory.CreateDirectory(store.Folder);
        File.WriteAllText(Path.Combine(store.Folder, "broken.json"), "{ not json");
        File.WriteAllText(Path.Combine(store.Folder, "dark.json"), """{ "name": "dark", "colors": { "accent": "#123456" } }""");

        Assert.Equal(ThemeDefinition.BuiltIns.Count, store.All().Count);
        Assert.Equal(ThemeDefinition.Dark.Color("accent"), store.Find("Dark").Color("accent"));
        Assert.Throws<InvalidOperationException>(() => store.Save(new ThemeDefinition("light", false, new Dictionary<string, string>())));
    }

    [Fact]
    public void SelectionIsKeptOutsideConfigJson()
    {
        var store = new ThemeStore(_dir);
        Assert.Null(store.SelectedName());
        store.Select("Light");
        Assert.Equal("Light", new ThemeStore(_dir).SelectedName());
        Assert.False(File.Exists(Path.Combine(_dir, "config.json")));
        Assert.Equal("Dark", store.Find("nonexistent").Name);
    }
}

public class ThemeContrastTests
{
    private static double Luminance(string hex)
    {
        static double Channel(int v) { var c = v / 255.0; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        return 0.2126 * Channel(Convert.ToInt32(hex[1..3], 16)) + 0.7152 * Channel(Convert.ToInt32(hex[3..5], 16)) + 0.0722 * Channel(Convert.ToInt32(hex[5..7], 16));
    }

    private static double Ratio(string a, string b)
    {
        var (hi, lo) = (Math.Max(Luminance(a), Luminance(b)), Math.Min(Luminance(a), Luminance(b)));
        return (hi + 0.05) / (lo + 0.05);
    }

    public static IEnumerable<object[]> Themes() => ThemeDefinition.BuiltIns.Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(Themes))]
    public void BuiltInThemesAreReadable(ThemeDefinition t)
    {
        void AtLeast(double min, string fg, string bg) =>
            Assert.True(Ratio(t.Color(fg), t.Color(bg)) >= min, $"{t.Name}: {fg} on {bg} = {Ratio(t.Color(fg), t.Color(bg)):0.0}");

        AtLeast(7, "text", "background");
        AtLeast(7, "text", "surface");
        AtLeast(4.5, "text", "raised");
        AtLeast(4.5, "textMuted", "background");
        AtLeast(4.5, "textMuted", "surface");
        AtLeast(4.5, "textMuted", "raised");
        AtLeast(4.5, "onAccent", "accent");
        AtLeast(4.5, "onAccent", "success");
        AtLeast(3, "accent", "background");
        AtLeast(4.5, "danger", "surface");
        AtLeast(4.5, "success", "surface");
        AtLeast(1.15, "border", "background");
    }
}

public class ThemeSelectionFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kt-themesel").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void FollowSystemAndChosenThemeKeepEachOther()
    {
        var store = new Kurotsuki.Core.Theming.ThemeStore(_dir);
        Assert.False(store.FollowsSystem());
        store.Select("Frost");
        store.SetFollowSystem(true);
        Assert.Equal("Frost", store.SelectedName());
        Assert.True(store.FollowsSystem());
        store.Select("Moss"); // choosing a theme must not switch "follow system" off
        Assert.True(store.FollowsSystem());
        Assert.Equal("Moss", store.SelectedName());
        store.SetFollowSystem(false);
        Assert.False(new Kurotsuki.Core.Theming.ThemeStore(_dir).FollowsSystem());
    }
}
