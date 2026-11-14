namespace Kurotsuki.Core.Booru;

/// <summary>A post as the JavaFX app stored it (favourites, download queue payloads): same field names.</summary>
public sealed class Post
{
    public long Id { get; set; }
    public int Score { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string? Image { get; set; }
    public string? Rating { get; set; }
    public string? Source { get; set; }
    public string? Tags { get; set; }
    public string? PreviewUrl { get; set; }
    public string? FileUrl { get; set; }
    public string? SampleUrl { get; set; }
    public string? BooruBaseUrl { get; set; }
    /// <summary>Parent and children, from the live API only (not saved: the favourites JSON is shared with the Java app).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public long ParentId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public bool HasChildren { get; set; }

    /// <summary>The search that shows this post together with its parent and siblings or children; null for a post on its own.</summary>
    public string? RelatedQuery => ParentId > 0 ? $"parent:{ParentId}" : HasChildren ? $"parent:{Id}" : null;

    public List<long> PoolIds { get; set; } = [];
    public Dictionary<string, TagCategory> TagCategories { get; set; } = [];

    /// <summary>Identifies the post across boorus; same format as the Java app (used in the "downloaded" table).</summary>
    public string Key => $"{BooruBaseUrl}#{Id}";

    public IEnumerable<string> TagList =>
        (Tags ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    public void AddTagCategories(string? tagString, TagCategory category)
    {
        if (string.IsNullOrWhiteSpace(tagString))
        {
            return;
        }
        foreach (var tag in tagString.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            TagCategories[tag] = category;
        }
    }

    public TagCategory CategoryOf(string tag) =>
        TagCategories.TryGetValue(tag, out var category) ? category : TagCategory.General;
}
