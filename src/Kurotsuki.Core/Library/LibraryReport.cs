using System.Globalization;
using System.Text;
using Kurotsuki.Core.Booru;

namespace Kurotsuki.Core.Library;

/// <summary>What can be said about a set of posts (the favourites): top tags, ratings, boorus.</summary>
public sealed record LibraryStats(int Posts, IReadOnlyList<(string Tag, int Count)> TopTags,
    IReadOnlyDictionary<string, int> Ratings, IReadOnlyDictionary<string, int> Boorus);

public static class LibraryReport
{
    public static LibraryStats Stats(IReadOnlyCollection<Post> posts, int topTags = 15)
    {
        var tags = new Dictionary<string, int>();
        var ratings = new Dictionary<string, int>();
        var boorus = new Dictionary<string, int>();
        foreach (var post in posts)
        {
            foreach (var tag in post.TagList.Distinct())
            {
                tags[tag] = tags.GetValueOrDefault(tag) + 1;
            }
            var rating = string.IsNullOrWhiteSpace(post.Rating) ? "?" : post.Rating.ToLowerInvariant();
            ratings[rating] = ratings.GetValueOrDefault(rating) + 1;
            var host = Uri.TryCreate(post.BooruBaseUrl, UriKind.Absolute, out var u) ? u.Host : post.BooruBaseUrl ?? "?";
            boorus[host] = boorus.GetValueOrDefault(host) + 1;
        }
        return new LibraryStats(posts.Count,
            [.. tags.OrderByDescending(t => t.Value).ThenBy(t => t.Key, StringComparer.Ordinal).Take(topTags).Select(t => (t.Key, t.Value))],
            ratings, boorus);
    }

    /// <summary>RFC 4180 CSV: key, id, booru, rating, score, width, height, file url, source, tags.</summary>
    public static string ToCsv(IEnumerable<Post> posts)
    {
        var sb = new StringBuilder("key,id,booru,rating,score,width,height,file_url,source,tags\r\n");
        foreach (var p in posts)
        {
            sb.AppendJoin(',', new[]
            {
                p.Key, p.Id.ToString(CultureInfo.InvariantCulture), p.BooruBaseUrl, p.Rating, p.Score.ToString(CultureInfo.InvariantCulture),
                p.Width.ToString(CultureInfo.InvariantCulture), p.Height.ToString(CultureInfo.InvariantCulture), p.FileUrl, p.Source, p.Tags,
            }.Select(Csv)).Append("\r\n");
        }
        return sb.ToString();
    }

    private static string Csv(string? value)
    {
        var v = value ?? "";
        // A leading = + - @ makes spreadsheets run the cell as a formula: defuse it, since tags and sources come from remote sites.
        if (v.Length > 0 && v[0] is '=' or '+' or '@' || v.StartsWith('-') && !long.TryParse(v, out _))
        {
            v = "'" + v;
        }
        return v.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }
}
