using System.Text.RegularExpressions;

namespace Kurotsuki.Core.Booru;

/// <summary>A post named by a pasted link or number. <see cref="Host"/> is null for a bare number (use the selected booru).</summary>
public sealed partial record PostLink(string? Host, long Id)
{
    [GeneratedRegex(@"^(?:(?:#|id:|post:)(\d{1,12})|(\d{4,12}))$", RegexOptions.IgnoreCase)]
    private static partial Regex Bare();

    [GeneratedRegex(@"/(?:posts|post/show|post/view|post)/(\d{1,12})(?:[/?#]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex PathId();

    /// <summary>Reads post links of every supported booru and ids ("#123", "id:123", or four or more digits alone); null for anything else.</summary>
    public static PostLink? Parse(string? text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0)
        {
            return null;
        }
        if (Bare().Match(t) is { Success: true } bare)
        {
            return new PostLink(null, long.Parse(bare.Groups[1].Success ? bare.Groups[1].Value : bare.Groups[2].Value));
        }
        if (!Uri.TryCreate(t, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return null;
        }
        // Gelbooru: index.php?page=post&s=view&id=123
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        if (query["page"] == "post" && query["s"] == "view" && long.TryParse(query["id"], out var gelbooru))
        {
            return new PostLink(uri.Host, gelbooru);
        }
        return PathId().Match(uri.AbsolutePath) is { Success: true } m ? new PostLink(uri.Host, long.Parse(m.Groups[1].Value)) : null;
    }
}
