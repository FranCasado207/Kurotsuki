using System.Globalization;
using System.Text;
using Kurotsuki.Core.Booru;

namespace Kurotsuki.Core.Download.Comic;

/// <summary>
/// Builds a ComicInfo.xml (Anansi Project schema v2.0) for a pool. Elements follow the order the schema's
/// <c>xs:sequence</c> demands, and anything we have no data for is left out rather than guessed.
/// </summary>
public static class ComicInfo
{
    public const string FileName = "ComicInfo.xml";
    private const int MaxGenres = 30;

    /// <summary>Tags e621 files under "artist" that are notices, not people.</summary>
    private static readonly HashSet<string> NotArtists =
    [
        "conditional_dnp", "sound_warning", "unknown_artist", "anonymous_artist", "avoid_posting", "epilepsy_warning",
        "unknown_artist_signature", "third-party_edit", "background_art", "sketch_page",
    ];

    private static readonly Dictionary<string, string> Languages = new()
    {
        ["english_text"] = "en", ["japanese_text"] = "ja", ["spanish_text"] = "es", ["french_text"] = "fr",
        ["german_text"] = "de", ["portuguese_text"] = "pt", ["italian_text"] = "it", ["russian_text"] = "ru",
        ["chinese_text"] = "zh", ["korean_text"] = "ko",
    };

    /// <summary>One page of the archive: the post it came from and the size of the stored file.</summary>
    public sealed record Page(Post Post, long Size);

    public static string Xml(Pool pool, IReadOnlyList<Page> pages, string webUrl, string publisher, string scanner)
    {
        var posts = pages.Select(p => p.Post).ToList();
        var artists = Count(posts, t => CategoryOf(posts, t) == TagCategory.Artist && !NotArtists.Contains(t));
        var characters = Count(posts, t => CategoryOf(posts, t) == TagCategory.Character);
        var copyrights = Count(posts, t => CategoryOf(posts, t) == TagCategory.Copyright);
        var general = Count(posts, t => CategoryOf(posts, t) == TagCategory.General);
        var firstPageArtists = posts.Count == 0 ? [] : posts[0].TagList
            .Where(t => posts[0].CategoryOf(t) == TagCategory.Artist && !NotArtists.Contains(t)).ToList();

        var xml = new XmlBuilder();
        xml.Text("Title", pool.DisplayName);
        xml.Text("Series", pool.DisplayName);
        xml.Text("Summary", pool.Description);
        xml.Text("Notes", $"Downloaded from {publisher} (pool #{pool.Id}, {pool.Category}) with {scanner}");
        if (Date(pool.CreatedAt ?? pool.UpdatedAt) is { } created)
        {
            xml.Number("Year", created.Year);
            xml.Number("Month", created.Month);
            xml.Number("Day", created.Day);
        }
        var artistNames = Join(artists.Select(a => Person(a)));
        xml.Text("Writer", artistNames);
        xml.Text("Penciller", artistNames);
        xml.Text("CoverArtist", Join(firstPageArtists.Select(Person)));
        xml.Text("Publisher", publisher);
        xml.Text("Genre", Join(general.Take(MaxGenres).Select(Words)));
        xml.Text("Web", webUrl);
        xml.Number("PageCount", pages.Count);
        xml.Text("LanguageISO", Language(posts));
        xml.Text("Format", "Web");
        xml.Text("BlackAndWhite", BlackAndWhite(posts));
        xml.Text("Manga", Share(posts, "manga") >= 0.5 ? "Yes" : null);
        xml.Text("Characters", Join(characters.Select(Words)));
        xml.Text("ScanInformation", scanner);
        xml.Text("SeriesGroup", Join(copyrights.Select(Words)));
        xml.Text("AgeRating", AgeRating(posts));
        xml.Raw(PagesElement(pages));
        xml.Text("MainCharacterOrTeam", characters.Take(1).Select(Words).FirstOrDefault());
        return xml.Build();
    }

    private static string PagesElement(IReadOnlyList<Page> pages)
    {
        if (pages.Count == 0)
        {
            return "";
        }
        var sb = new StringBuilder("  <Pages>\n");
        for (var i = 0; i < pages.Count; i++)
        {
            var post = pages[i].Post;
            sb.Append($"    <Page Image=\"{i}\"");
            if (i == 0)
            {
                sb.Append(" Type=\"FrontCover\"");
            }
            sb.Append($" ImageSize=\"{pages[i].Size}\"");
            if (post.Width > 0 && post.Height > 0)
            {
                sb.Append($" ImageWidth=\"{post.Width}\" ImageHeight=\"{post.Height}\"");
            }
            sb.Append($" Key=\"{post.Id}\" />\n");
        }
        return sb.Append("  </Pages>\n").ToString();
    }

    private static string? AgeRating(List<Post> posts)
    {
        var worst = -1;
        foreach (var post in posts)
        {
            var rating = (post.Rating ?? "").ToLowerInvariant();
            worst = Math.Max(worst, rating.StartsWith('e') ? 2 : rating.StartsWith('q') ? 1
                : rating.StartsWith('s') || rating.StartsWith('g') ? 0 : -1);
        }
        return worst switch { 0 => "Everyone", 1 => "Mature 17+", 2 => "Adults Only 18+", _ => null };
    }

    private static string? BlackAndWhite(List<Post> posts)
    {
        if (posts.Count == 0)
        {
            return null;
        }
        var share = Math.Max(Share(posts, "monochrome"), Share(posts, "greyscale"));
        return share >= 1.0 ? "Yes" : share == 0.0 ? "No" : "Unknown";
    }

    private static string? Language(List<Post> posts) => posts.SelectMany(p => p.TagList)
        .Where(Languages.ContainsKey).GroupBy(t => Languages[t]).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();

    private static double Share(List<Post> posts, string tag) =>
        posts.Count == 0 ? 0 : (double)posts.Count(p => p.TagList.Contains(tag)) / posts.Count;

    private static TagCategory CategoryOf(List<Post> posts, string tag) =>
        posts.FirstOrDefault(p => p.TagCategories.ContainsKey(tag))?.CategoryOf(tag) ?? TagCategory.General;

    /// <summary>Tags that pass <paramref name="keep"/>, most frequent (by number of pages carrying them) first.</summary>
    private static List<string> Count(List<Post> posts, Func<string, bool> keep) => posts
        .SelectMany(p => p.TagList.Distinct()).Where(keep).GroupBy(t => t)
        .OrderByDescending(g => g.Count()).Select(g => g.Key).ToList();

    private static DateTimeOffset? Date(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>"some_artist_(artist)" becomes "some artist".</summary>
    private static string Person(string tag) => Words(tag.EndsWith("_(artist)") ? tag[..^9] : tag);
    private static string Words(string tag) => tag.Replace('_', ' ');
    private static string? Join(IEnumerable<string> values) => values.ToList() is { Count: > 0 } l ? string.Join(", ", l) : null;

    private sealed class XmlBuilder
    {
        private readonly StringBuilder _out = new("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
            + "<ComicInfo xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">\n");

        public void Text(string element, string? value)
        {
            var cleaned = value is null ? "" : Escape(value).Trim();
            if (cleaned.Length > 0)
            {
                _out.Append($"  <{element}>{cleaned}</{element}>\n");
            }
        }

        public void Number(string element, int value) => _out.Append($"  <{element}>{value}</{element}>\n");
        public void Raw(string fragment) => _out.Append(fragment);
        public string Build() => _out.Append("</ComicInfo>\n").ToString();
    }

    /// <summary>Escapes markup characters and drops characters XML 1.0 cannot represent.</summary>
    internal static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            var c = rune.Value;
            var legal = c is 0x9 or 0xA or 0xD || (c >= 0x20 && c <= 0xD7FF) || (c >= 0xE000 && c <= 0xFFFD) || (c >= 0x10000 && c <= 0x10FFFF);
            if (!legal)
            {
                continue;
            }
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&apos;"); break;
                default: sb.Append(rune.ToString()); break;
            }
        }
        return sb.ToString();
    }
}
