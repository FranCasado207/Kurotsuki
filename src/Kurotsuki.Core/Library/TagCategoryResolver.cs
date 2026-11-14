using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Storage;

namespace Kurotsuki.Core.Library;

/// <summary>
/// Fills in tag categories for posts whose booru did not send them (Gelbooru, Moebooru, Szurubooru). Known answers
/// come from the <c>tag_categories</c> cache table first; only the rest is asked of the server, then cached.
/// </summary>
public static class TagCategoryResolver
{
    public static async Task ResolveAsync(Post post, IBooruClient client, LibraryStore? store, CancellationToken ct = default)
    {
        var missing = post.TagList.Distinct().Where(t => !post.TagCategories.ContainsKey(t)).ToList();
        if (missing.Count == 0 || post.BooruBaseUrl is null)
        {
            return;
        }
        if (store is not null)
        {
            foreach (var (tag, category) in store.TagCategories(post.BooruBaseUrl, missing))
            {
                post.TagCategories[tag] = category;
            }
            missing = missing.Where(t => !post.TagCategories.ContainsKey(t)).ToList();
        }
        if (missing.Count == 0)
        {
            return;
        }
        var fetched = await client.TagCategoriesAsync(missing, ct);
        foreach (var (tag, category) in fetched)
        {
            post.TagCategories[tag] = category;
        }
        if (store is not null && fetched.Count > 0)
        {
            store.SaveTagCategories(post.BooruBaseUrl, fetched);
        }
    }
}
