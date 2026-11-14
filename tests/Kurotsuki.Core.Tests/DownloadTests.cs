using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Download;
using Kurotsuki.Core.Net;
using Kurotsuki.Core.Storage;

namespace Kurotsuki.Core.Tests;

public class DownloadTests
{
    private static Post Sample(string image = "0123456789abcdef0123456789abcdef.jpg") => new()
    {
        Id = 99, Score = 5, Image = image, Rating = "general", Tags = "tag_a tag_b", Source = "https://src.example/1",
        BooruBaseUrl = "https://example.booru", FileUrl = "https://example.booru/f/" + image,
        TagCategories = { ["tag_a"] = TagCategory.Artist },
    };

    [Fact]
    public void TemplateSubstitutesTokens()
    {
        var path = new FileNameTemplate("%booru%/%artist%/%id%_%md5%.%ext%").Render(Sample());
        Assert.Equal("example.booru/tag_a/99_0123456789abcdef0123456789abcdef.jpg", path);
    }

    [Fact]
    public void TemplateAddsMissingExtensionAndDefaultsToFileName()
    {
        Assert.Equal("99.jpg", new FileNameTemplate("%id%").Render(Sample()));
        Assert.Equal("0123456789abcdef0123456789abcdef.jpg", new FileNameTemplate("").Render(Sample()));
    }

    [Fact]
    public void HostileValuesCannotEscapeTheFolder()
    {
        var post = Sample("../../etc/passwd");
        post.TagCategories = new() { ["../../x"] = TagCategory.Artist };
        var relative = new FileNameTemplate("%artist%/%file%").Render(post);
        Assert.DoesNotContain("..", relative.Split('/'));
        var dir = Path.Combine(Path.GetTempPath(), "kt-dl-" + Guid.NewGuid().ToString("N"));
        Assert.StartsWith(Path.GetFullPath(dir), FileNameTemplate.UniqueTarget(dir, relative));
        Assert.Throws<IOException>(() => FileNameTemplate.UniqueTarget(dir, "../outside.jpg"));
    }

    [Fact]
    public void UniqueTargetNumbersExistingAndReservedFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kt-dl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.jpg"), "x");
        var reserved = new HashSet<string> { Path.Combine(dir, "a (1).jpg") };
        Assert.Equal(Path.Combine(dir, "a (2).jpg"), FileNameTemplate.UniqueTarget(dir, "a.jpg", reserved));
    }

    [Fact]
    public void SidecarFormatsMatchTheJavaOnes()
    {
        Assert.Equal("tag_a\ntag_b\nsource:https://src.example/1\n", Sidecar.Text(Sample()));
        var json = Sidecar.Json(Sample());
        Assert.Contains("\"tagCategories\"", json);
        Assert.Contains("\"tag_a\": \"artist\"", json);
    }

    [Theory]
    [InlineData("120", 120)]
    [InlineData("abc", null)]
    [InlineData(null, null)]
    public void RetryAfterSeconds(string? header, int? seconds) =>
        Assert.Equal(seconds is null ? null : TimeSpan.FromSeconds(seconds.Value),
            MediaDownloader.ParseRetryAfter(header, DateTimeOffset.UtcNow));

    [Fact]
    public void RetryAfterHttpDate()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromSeconds(30),
            MediaDownloader.ParseRetryAfter("Thu, 01 Jan 2026 00:00:30 GMT", now));
        Assert.Equal(TimeSpan.Zero, MediaDownloader.ParseRetryAfter("Wed, 31 Dec 2025 23:00:00 GMT", now));
    }

    [Fact]
    public void ThrottleSpacesRequestsPerHost()
    {
        long now = 0;
        var throttle = new HostThrottle(TimeSpan.FromTicks(100), () => now);
        Assert.Equal(TimeSpan.Zero, throttle.Reserve("a"));
        Assert.Equal(TimeSpan.FromTicks(100), throttle.Reserve("a"));
        Assert.Equal(TimeSpan.Zero, throttle.Reserve("b"));
    }

    [Fact]
    public void StoreKeepsDownloadedKeysAndLeavesOtherQueueKindsAlone()
    {
        var file = Path.Combine(Path.GetTempPath(), "kt-db-" + Guid.NewGuid().ToString("N") + ".db");
        using (var store = new LibraryStore(file))
        {
            store.MarkDownloaded("k#1", 1);
            store.SaveQueue([new("k#1", "post", "{}")]);
        }
        using (var store = new LibraryStore(file))
        {
            Assert.Contains("k#1", store.DownloadedKeys());
            Assert.Single(store.QueueEntries());
            store.ForgetDownloads();
            Assert.Empty(store.DownloadedKeys());
        }
    }

    [Fact]
    public void ImageCacheNamesFilesLikeTheJavaApp()
    {
        // sha256("") — the Java CacheManager used lowercase hex of the SHA-256 of the URL
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", ImageCache.Hash(""));
    }
}
