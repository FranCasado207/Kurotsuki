using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Download;
using Kurotsuki.Core.Library;
using Kurotsuki.Core.Session;

namespace Kurotsuki.Core.Tests;

public class NextSettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kt-next-settings").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void EverythingSurvivesARestart()
    {
        var a = new NextSettings(_dir);
        a.SetSafeMode("booru-1", true);
        a.SaveSearch(new("cats", "booru-1", "cat rating:g"));
        a.SetZoom("pool#1", 2.5);
        Assert.True(a.ToggleBookmark("pool#1", 7));
        Assert.True(a.ToggleBookmark("pool#1", 3));

        var b = new NextSettings(_dir);
        Assert.True(b.IsSafeMode("booru-1"));
        Assert.False(b.IsSafeMode("other"));
        Assert.Equal(new SavedSearch("cats", "booru-1", "cat rating:g"), Assert.Single(b.SavedSearches));
        Assert.Equal(2.5, b.ZoomOf("pool#1"));
        Assert.Null(b.ZoomOf("pool#2"));
        Assert.Equal([3, 7], b.BookmarksOf("pool#1"));
    }

    [Fact]
    public void SavingTheSameNameReplacesAndBookmarksToggle()
    {
        var s = new NextSettings(_dir);
        s.SaveSearch(new("x", null, "one"));
        s.SaveSearch(new("X", null, "two"));
        Assert.Equal("two", Assert.Single(s.SavedSearches).Tags);
        s.DeleteSearch("x");
        Assert.Empty(s.SavedSearches);

        Assert.True(s.ToggleBookmark("p", 1));
        Assert.False(s.ToggleBookmark("p", 1));
        Assert.Empty(s.BookmarksOf("p"));
    }

    [Fact]
    public void ACorruptFileStartsEmpty()
    {
        File.WriteAllText(Path.Combine(_dir, "next-settings.json"), "][");
        var s = new NextSettings(_dir);
        Assert.Empty(s.SavedSearches);
        s.SetSafeMode("b", true);
        Assert.True(new NextSettings(_dir).IsSafeMode("b"));
    }
}

public class LibraryHelpersTests
{
    private static Post P(long id, string tags, string rating = "g", string md5 = "") => new()
    {
        Id = id, Tags = tags, Rating = rating, BooruBaseUrl = "https://b.example", Image = md5.Length == 0 ? null : md5 + ".jpg",
        FileUrl = "https://f/x", Source = "=cmd|' /C calc'!A0",
    };

    [Theory]
    [InlineData("https://danbooru.donmai.us/posts/123?q=cat", "danbooru.donmai.us", 123)]
    [InlineData("https://gelbooru.com/index.php?page=post&s=view&id=77&tags=x", "gelbooru.com", 77)]
    [InlineData("https://yande.re/post/show/9/tag", "yande.re", 9)]
    [InlineData("https://s.example/post/view/5", "s.example", 5)]
    [InlineData("https://sz.example/post/42", "sz.example", 42)]
    [InlineData("#31", null, 31)]
    [InlineData("id:8", null, 8)]
    [InlineData("  55123 ", null, 55123)]
    public void PostLinksAndIdsAreRecognised(string text, string? host, long id) =>
        Assert.Equal(new PostLink(host, id), PostLink.Parse(text));

    [Theory]
    [InlineData("cat ears")]
    [InlineData("1girl")]
    [InlineData("55")]
    [InlineData("https://example.com/about")]
    [InlineData("javascript:alert(1)")]
    [InlineData("")]
    [InlineData(null)]
    public void OtherTextIsNotAPostLink(string? text) => Assert.Null(PostLink.Parse(text));

    [Fact]
    public void DuplicateIndexRecognisesTheSameFileFromAnotherBooruAndSurvivesRestarts()
    {
        var dir = Directory.CreateTempSubdirectory("kt-dup").FullName;
        try
        {
            var file = Path.Combine(dir, "sub", "hashes.txt");
            var md5 = "0123456789abcdef0123456789ABCDEF";
            var index = new DuplicateIndex(file);
            Assert.False(index.Contains(P(1, "a", md5: md5)));
            index.Add(P(1, "a", md5: md5));
            index.Add(P(1, "a", md5: md5));
            Assert.True(index.Contains(new Post { Id = 99, BooruBaseUrl = "https://other", Image = md5.ToLowerInvariant() + ".png" }));
            Assert.False(index.Contains(P(2, "a"))); // no md5: never a duplicate
            Assert.False(index.Contains(new Post { Image = "not-a-hash.jpg" }));

            var again = new DuplicateIndex(file);
            Assert.Equal(1, again.Count);
            Assert.True(again.Contains(P(3, "z", md5: md5)));
            again.Clear();
            Assert.Equal(0, new DuplicateIndex(file).Count);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void StatsCountTagsRatingsAndBoorus()
    {
        var stats = LibraryReport.Stats([P(1, "cat dog"), P(2, "cat bird", "e"), P(3, "cat")], topTags: 2);
        Assert.Equal(3, stats.Posts);
        Assert.Equal([("cat", 3), ("bird", 1)], stats.TopTags);
        Assert.Equal(2, stats.Ratings["g"]);
        Assert.Equal(3, stats.Boorus["b.example"]);
    }

    [Fact]
    public void CsvQuotesFieldsAndDefusesSpreadsheetFormulas()
    {
        var csv = LibraryReport.ToCsv([new Post { Id = 1, Tags = "a,b \"q\"", Source = "=HYPERLINK(1)", BooruBaseUrl = "https://x" }]);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"a,b \"\"q\"\"\"", lines[1]);
        Assert.Contains("'=HYPERLINK(1)", lines[1]);
    }

    [Fact]
    public void WatchedSearchesKeepTheHighestSeenId()
    {
        var dir = Directory.CreateTempSubdirectory("kt-watch").FullName;
        try
        {
            var s = new NextSettings(dir);
            s.Watch(new WatchedSearch("artist", "b", "artist_x"));
            s.MarkSeen("artist", 50);
            s.MarkSeen("artist", 40);
            Assert.Equal(50, new NextSettings(dir).Watched.Single().LastSeenId);
            s.Unwatch("ARTIST");
            Assert.Empty(new NextSettings(dir).Watched);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
