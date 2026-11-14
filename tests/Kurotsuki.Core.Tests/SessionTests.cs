using Kurotsuki.Core.Session;

namespace Kurotsuki.Core.Tests;

public class SessionTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kt-session").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private static SessionState Sample() => new(
        [new(TabKinds.Search, "a|b", "cat ears", Scroll: 1234.5), new(TabKinds.Favorites)], Selected: 1, Booru: "a|b",
        SearchText: "dog", PoolMode: true);

    [Fact]
    public void OffByDefaultAndNothingIsStoredWhileOff()
    {
        var store = new SessionStore(_dir);
        Assert.False(store.Enabled);
        store.Save(Sample());
        Assert.Null(store.Load());
        Assert.False(File.Exists(Path.Combine(_dir, "next-session.json")));
    }

    [Fact]
    public void SavedStateComesBackExactly()
    {
        var store = new SessionStore(_dir);
        store.SetEnabled(true);
        Assert.Null(store.Load());
        store.Save(Sample());

        var loaded = new SessionStore(_dir).Load()!;
        Assert.Equal(2, loaded.Tabs.Count);
        Assert.Equal(new SessionTab(TabKinds.Search, "a|b", "cat ears", Scroll: 1234.5), loaded.Tabs[0]);
        Assert.Equal(1, loaded.Selected);
        Assert.Equal("dog", loaded.SearchText);
        Assert.True(loaded.PoolMode);
    }

    [Fact]
    public void TurningItOffForgetsTheState()
    {
        var store = new SessionStore(_dir);
        store.SetEnabled(true);
        store.Save(Sample());
        store.SetEnabled(false);
        store.SetEnabled(true);
        Assert.Null(store.Load());
    }

    [Fact]
    public void ACorruptFileCountsAsNothingSaved()
    {
        File.WriteAllText(Path.Combine(_dir, "next-session.json"), "{ not json");
        var store = new SessionStore(_dir);
        Assert.False(store.Enabled);
        Assert.Null(store.Load());
        store.SetEnabled(true);
        Assert.True(store.Enabled);
    }
}
