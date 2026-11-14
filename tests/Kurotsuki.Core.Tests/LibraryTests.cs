using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Library;
using Kurotsuki.Core.Storage;

namespace Kurotsuki.Core.Tests;

public class LibraryTests
{
    private static string TempDb() => Path.Combine(Path.GetTempPath(), "kt-lib-" + Guid.NewGuid().ToString("N") + ".db");

    private static Post Sample(long id, string tags = "a b") => new()
    {
        Id = id, Score = 3, Width = 10, Height = 20, Image = $"{id}.png", Rating = "general", Tags = tags,
        BooruBaseUrl = "https://x.example", FileUrl = $"https://x.example/{id}.png", PreviewUrl = $"https://x.example/t{id}.png",
        TagCategories = { ["a"] = TagCategory.Artist, ["b"] = TagCategory.General },
    };

    [Fact]
    public void FavoritePostsRoundTripWithTagCategoriesAndKeepOrder()
    {
        using var store = new LibraryStore(TempDb());
        store.SetFavoritePost(Sample(2), true);
        store.SetFavoritePost(Sample(1), true);

        var posts = store.FavoritePosts();
        Assert.Equal([2L, 1L], posts.Select(p => p.Id));
        Assert.Equal(TagCategory.Artist, posts[0].CategoryOf("a"));
        Assert.True(store.IsFavoritePost("https://x.example#2"));

        store.SetFavoritePost(Sample(2), false);
        Assert.Single(store.FavoritePosts());
        // an unused post row is pruned, like the Java app does
        Assert.False(store.IsFavoritePost("https://x.example#2"));
    }

    [Fact]
    public void FoldersKeepPostsEvenAfterUnfavoriting()
    {
        using var store = new LibraryStore(TempDb());
        store.CreateFolder("f");
        store.AddToFolder("f", Sample(5));
        store.SetFavoritePost(Sample(5), true);
        store.SetFavoritePost(Sample(5), false);

        Assert.Equal([5L], store.Folders()["f"].Select(p => p.Id));
        store.RemoveFromFolder("f", Sample(5));
        Assert.Empty(store.Folders()["f"]);
        store.DeleteFolder("f");
        Assert.Empty(store.Folders());
    }

    [Fact]
    public void TagGroupsRequireFavoriteTagsAndFollowThemOut()
    {
        using var store = new LibraryStore(TempDb());
        store.AddFavoriteTag("cat");
        store.CreateTagGroup("Animals");
        store.AddTagToGroup("Animals", "cat");
        // a tag that is not a favourite violates the foreign key, as in the Java app
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => store.AddTagToGroup("Animals", "not_a_favorite"));
        Assert.Equal(["cat"], store.TagGroups()["Animals"]);

        store.RemoveFavoriteTag("cat");
        Assert.Empty(store.TagGroups()["Animals"]);
    }

    [Fact]
    public void ReadingProgressRoundTrips()
    {
        using var store = new LibraryStore(TempDb());
        var record = new ReadingRecord("https://x.example#pool:9", "https://x.example", 9, "Comic", "series", 3, 10, false, 1000);
        store.SaveReading(record);
        store.SaveReading(record with { Page = 9, Finished = true, UpdatedAt = 2000 });

        var loaded = Assert.Single(store.ReadingRecords());
        Assert.True(loaded.Finished);
        Assert.Equal(9, loaded.Page);
        store.DeleteReading(record.PoolKey);
        Assert.Empty(store.ReadingRecords());
    }

    [Fact]
    public void ConfigImportRunsOnlyOnce()
    {
        using var store = new LibraryStore(TempDb());
        Assert.True(store.MigrateFromConfig(["t1"], [Sample(1)], [("F", new[] { Sample(2) })]));
        Assert.False(store.MigrateFromConfig(["t2"], null, null));
        Assert.Equal(["t1"], store.FavoriteTags());
        Assert.Single(store.FavoritePosts());
        Assert.Single(store.Folders()["F"]);
    }

    [Fact]
    public void TagCategoryCacheStoresJavaStyleNames()
    {
        using var store = new LibraryStore(TempDb());
        store.SaveTagCategories("https://x.example", new Dictionary<string, TagCategory> { ["bob"] = TagCategory.Artist });
        Assert.Equal(TagCategory.Artist, store.TagCategories("https://x.example", ["bob", "nope"])["bob"]);
        Assert.Single(store.TagCategories("https://x.example", ["bob", "nope"]));
    }

    [Fact]
    public void TheSameFileSurvivesBeingOpenedTwiceAndKeepsUnknownQueueKinds()
    {
        var file = TempDb();
        using (var store = new LibraryStore(file))
        {
            store.SaveQueue([new("k#1", "post", "{}")]);
        }
        using (var store = new LibraryStore(file))
        {
            Assert.Single(store.QueueEntries());
        }
    }

    [Fact]
    public void ServicesPersistAndUndoOnFailure()
    {
        var errors = new List<string>();
        var store = new LibraryStore(TempDb());
        var favorites = new Favorites(store, errors.Add);
        var groups = new TagGroups(store, favorites, errors.Add);
        var folders = new PostFolders(store, errors.Add);
        var reading = new PoolReading(store, errors.Add);

        var group = groups.Create("G");
        groups.AddTag(group, "cat"); // makes "cat" a favourite first
        Assert.True(favorites.IsTagFavorite("cat"));
        Assert.Equal("G", groups.GroupFor(new Post { Tags = "x cat" }));
        Assert.False(groups.Rename(group, " ") );
        Assert.True(groups.Rename(group, "Cats"));
        favorites.ToggleTag("cat");
        Assert.Empty(groups.Find("Cats")!.Tags);

        Assert.True(folders.Create("Keep"));
        Assert.False(folders.Create("Keep"));

        var pool = new Pool(7, "A_pool", null, "series", 5, [1, 2, 3, 4, 5], "https://x.example", null, null);
        reading.Record(pool, 2, 5);
        Assert.False(reading.IsFinished(pool.Key));
        reading.Record(pool, 4, 5);
        Assert.True(reading.IsFinished(pool.Key));
        reading.Record(pool, 0, 5); // stays finished until reset
        Assert.True(reading.IsFinished(pool.Key));
        reading.SetFinished(pool, false);
        Assert.False(reading.IsFinished(pool.Key));

        // after the store is gone, writes fail: the change is undone and the user told
        store.Dispose();
        favorites.ToggleTag("late");
        Assert.DoesNotContain("late", favorites.Tags);
        Assert.NotEmpty(errors);
    }
}

public class ReaderHelperTests
{
    [Fact]
    public void SinglesAndSpreadsPairLikeABook()
    {
        Assert.Equal(3, SpreadLayout.Singles(3).Count);
        var wide = new HashSet<int> { 3 };
        var groups = SpreadLayout.Spreads(7, wide.Contains, coverAlone: true);
        // 0 | 1 2 | 3(wide) | 4 5 | 6
        Assert.Equal(["0", "1,2", "3", "4,5", "6"], groups.Select(g => string.Join(',', g)));
        Assert.Equal(3, SpreadLayout.GroupOf(groups, 5));
        Assert.Equal(groups.Count - 1, SpreadLayout.GroupOf(groups, 99));
        Assert.Equal(["0,1", "2", "3", "4,5", "6"], SpreadLayout.Spreads(7, wide.Contains, coverAlone: false).Select(g => string.Join(',', g)));
    }

    [Fact]
    public void PageBudgetEvictsLeastRecentlyUsedButNeverProtectedPages()
    {
        var budget = new PageBudget(100);
        budget.Add(0, 40);
        budget.Add(1, 40);
        budget.Add(2, 40);
        budget.Touch(0); // 1 is now the oldest
        Assert.Equal(120, budget.Total);

        var dropped = budget.Evict(new HashSet<int> { 2 });
        Assert.Equal([1], dropped);
        Assert.Equal(80, budget.Total);
        Assert.Empty(budget.Evict(new HashSet<int>())); // fits now
        budget.Add(3, 100);
        Assert.Equal([2, 0], budget.Evict(new HashSet<int> { 3 })); // everything else goes, least recently used first
        Assert.Equal(4 * 6 * 4, PageBudget.Estimate(4, 6) );
    }
}
