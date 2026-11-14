using System.Text.Json;
using Kurotsuki.Core.Logging;

namespace Kurotsuki.Core.Session;

/// <summary>A search the user kept; <see cref="Booru"/> is <c>BooruConfiguration.Identity</c> (null = whichever is selected).</summary>
public sealed record SavedSearch(string Name, string? Booru, string Tags);

/// <summary>A search the app checks for new posts. <see cref="LastSeenId"/> is the highest post id seen; 0 before the first check.</summary>
public sealed record WatchedSearch(string Name, string? Booru, string Tags, long LastSeenId = 0);

/// <summary>
/// Small per-user settings that only this app has: safe mode per booru, saved searches, and reader memory (zoom and bookmarks per
/// pool). They live in <c>next-settings.json</c> next to config.json because config.json and the database belong to the JavaFX app
/// as well and must not gain fields or tables it does not know.
/// </summary>
public sealed class NextSettings(string configDirectory)
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public sealed class Data
    {
        public HashSet<string> SafeMode { get; set; } = [];
        public List<SavedSearch> SavedSearches { get; set; } = [];
        public double TextSize { get; set; } = DefaultTextSize;
        public List<WatchedSearch> Watched { get; set; } = [];
        public Dictionary<string, double> ReaderZoom { get; set; } = [];
        public Dictionary<string, List<int>> Bookmarks { get; set; } = [];
    }

    public const double DefaultTextSize = 14;

    /// <summary>Base text size in pixels, 10 to 24.</summary>
    public double TextSize => Current.TextSize is >= 10 and <= 24 ? Current.TextSize : DefaultTextSize;

    public void SetTextSize(double size)
    {
        if (Math.Abs(TextSize - size) < 0.01 || size is < 10 or > 24)
        {
            return;
        }
        Current.TextSize = size;
        Save();
    }

    private Data? _data;
    private string Path_ => Path.Combine(configDirectory, "next-settings.json");

    private Data Current => _data ??= Load();

    private Data Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Data>(File.ReadAllText(Path_), Options) ?? new Data();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Data();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(configDirectory);
            var temp = Path_ + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Current, Options));
            File.Move(temp, Path_, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Warning("Could not save next-settings.json", e);
        }
    }

    // ---- safe mode: Explicit posts start hidden in searches of that booru ------------------------------------

    public bool IsSafeMode(string booru) => Current.SafeMode.Contains(booru);

    public void SetSafeMode(string booru, bool on)
    {
        if (on ? Current.SafeMode.Add(booru) : Current.SafeMode.Remove(booru))
        {
            Save();
        }
    }

    // ---- saved searches -------------------------------------------------------------------------------------

    public IReadOnlyList<SavedSearch> SavedSearches => Current.SavedSearches;

    /// <summary>Adds the search, or replaces the one with the same name.</summary>
    public void SaveSearch(SavedSearch search)
    {
        var list = Current.SavedSearches;
        var index = list.FindIndex(s => s.Name.Equals(search.Name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            list[index] = search;
        }
        else
        {
            list.Add(search);
        }
        Save();
    }

    public void DeleteSearch(string name)
    {
        if (Current.SavedSearches.RemoveAll(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            Save();
        }
    }

    // ---- watched searches -----------------------------------------------------------------------------------

    public IReadOnlyList<WatchedSearch> Watched => Current.Watched;

    public void Watch(WatchedSearch search)
    {
        var list = Current.Watched;
        var index = list.FindIndex(w => w.Name.Equals(search.Name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            list[index] = search;
        }
        else
        {
            list.Add(search);
        }
        Save();
    }

    public void Unwatch(string name)
    {
        if (Current.Watched.RemoveAll(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            Save();
        }
    }

    public void MarkSeen(string name, long lastSeenId)
    {
        var index = Current.Watched.FindIndex(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0 && Current.Watched[index].LastSeenId < lastSeenId)
        {
            Current.Watched[index] = Current.Watched[index] with { LastSeenId = lastSeenId };
            Save();
        }
    }

    // ---- reader memory --------------------------------------------------------------------------------------

    public double? ZoomOf(string poolKey) => Current.ReaderZoom.TryGetValue(poolKey, out var z) && double.IsFinite(z) ? z : null;

    public void SetZoom(string poolKey, double zoom)
    {
        if (Current.ReaderZoom.TryGetValue(poolKey, out var old) && Math.Abs(old - zoom) < 0.001)
        {
            return;
        }
        Current.ReaderZoom[poolKey] = zoom;
        Save();
    }

    /// <summary>Bookmarked pages (0-based), ascending.</summary>
    public IReadOnlyList<int> BookmarksOf(string poolKey) =>
        Current.Bookmarks.TryGetValue(poolKey, out var pages) ? pages : [];

    /// <returns>True when the page is bookmarked afterwards.</returns>
    public bool ToggleBookmark(string poolKey, int page)
    {
        if (!Current.Bookmarks.TryGetValue(poolKey, out var pages))
        {
            Current.Bookmarks[poolKey] = pages = [];
        }
        var now = !pages.Remove(page);
        if (now)
        {
            pages.Add(page);
            pages.Sort();
        }
        if (pages.Count == 0)
        {
            Current.Bookmarks.Remove(poolKey);
        }
        Save();
        return now;
    }
}
