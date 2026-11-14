using Kurotsuki.Core.Booru;

namespace Kurotsuki.Core.Config;

public static class TagBlacklist
{
    public static string Normalize(string? tag) =>
        tag is null ? "" : tag.Trim().ToLowerInvariant().Replace(' ', '_');

    public static bool IsBlocked(Post post, ICollection<string>? blacklist)
    {
        if (blacklist is null || blacklist.Count == 0 || post.Tags is null)
        {
            return false;
        }
        return post.TagList.Any(tag => blacklist.Contains(Normalize(tag)));
    }

    /// <summary>The blacklisted tags a post carries, in the post's own order. Empty when nothing matches.</summary>
    public static IReadOnlyList<string> MatchingTags(Post post, ICollection<string>? blacklist)
    {
        if (blacklist is null || blacklist.Count == 0 || post.Tags is null)
        {
            return [];
        }
        return post.TagList.Select(Normalize).Where(blacklist.Contains).Distinct().ToList();
    }

    /// <summary>Blocked by the blacklist, unless the user reviewed this very post and chose to keep it.</summary>
    public static bool IsBlocked(Post post, ICollection<string>? blacklist, Library.BlacklistExceptions? exceptions) =>
        IsBlocked(post, blacklist) && exceptions?.IsAllowed(post.Key) != true;
}
