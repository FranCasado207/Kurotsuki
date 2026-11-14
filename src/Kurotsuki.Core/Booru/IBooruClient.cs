namespace Kurotsuki.Core.Booru;

public interface IBooruClient
{
    /// <summary>One page of results; <paramref name="page"/> starts at 0.</summary>
    Task<IReadOnlyList<Post>> SearchPostsAsync(string? tags, int page, int limit, CancellationToken ct = default);

    Task<IReadOnlyList<string>> SuggestTagsAsync(string prefix, int limit, CancellationToken ct = default);

    /// <summary>Wiki text, aliases and implications of a tag; null when this booru has none to offer.</summary>
    Task<TagInfo?> TagInfoAsync(string tag, CancellationToken ct = default) => Task.FromResult<TagInfo?>(null);

    /// <summary>Whether this booru has pools (ordered collections such as comics).</summary>
    bool SupportsPools => false;

    /// <summary>False when the booru ignores the category and order arguments of <see cref="SearchPoolsAsync"/>.</summary>
    bool SupportsPoolFilters => true;

    /// <summary>
    /// How many pools one page holds if the server fixes it (so a shorter page means the last one); with
    /// <paramref name="requested"/> the client's own limit applies.
    /// </summary>
    int PoolPageSize(int requested) => requested;

    /// <param name="category"><see cref="Pool.Series"/>, <see cref="Pool.Collection"/> or null for both; <paramref name="page"/> starts at 0.</param>
    Task<IReadOnlyList<Pool>> SearchPoolsAsync(string? nameContains, string? category, PoolOrder order, int page, int limit,
        CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Pool>>([]);

    Task<Pool?> GetPoolAsync(long id, CancellationToken ct = default) => Task.FromResult<Pool?>(null);

    /// <summary>The pools this post is part of; empty when it is in none or the booru cannot tell. May make requests.</summary>
    Task<IReadOnlyList<Pool>> PoolsOfAsync(Post post, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Pool>>([]);

    /// <summary>The first post of each pool, keyed by pool id, for showing a cover. Pools without a known cover are left out.</summary>
    Task<IReadOnlyDictionary<long, Post>> PoolCoversAsync(IReadOnlyList<Pool> pools, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<long, Post>>(new Dictionary<long, Post>());

    /// <summary>All posts of the pool that the account can see, in the pool's reading order.</summary>
    Task<IReadOnlyList<Post>> PoolPostsAsync(Pool pool, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Post>>([]);

    /// <summary>Categories for tags whose posts did not carry them (Gelbooru, Moebooru, Szurubooru).</summary>
    Task<IReadOnlyDictionary<string, TagCategory>> TagCategoriesAsync(IEnumerable<string> names, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<string, TagCategory>>(new Dictionary<string, TagCategory>());
}
