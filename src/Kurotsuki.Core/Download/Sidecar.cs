using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kurotsuki.Core.Booru;

namespace Kurotsuki.Core.Download;

/// <summary>Metadata file written next to a download so tools such as Hydrus can import the tags.</summary>
public static partial class Sidecar
{
    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string PathFor(string media, SidecarFormat format) => media + format.Suffix();

    public static async Task WriteAsync(Post post, string media, SidecarFormat format)
    {
        if (format == SidecarFormat.None)
        {
            return;
        }
        var content = format == SidecarFormat.Txt ? Text(post) : Json(post);
        await File.WriteAllTextAsync(PathFor(media, format), content, new UTF8Encoding(false));
    }

    internal static string Text(Post post)
    {
        var sb = new StringBuilder();
        foreach (var tag in post.TagList)
        {
            sb.Append(tag).Append('\n');
        }
        if (!string.IsNullOrWhiteSpace(post.Source))
        {
            sb.Append("source:").Append(LineBreaks().Replace(post.Source.Trim(), " ")).Append('\n');
        }
        return sb.ToString();
    }

    internal static string Json(Post post)
    {
        var data = new Dictionary<string, object?>
        {
            ["booru"] = post.BooruBaseUrl,
            ["id"] = post.Id,
            ["url"] = post.FileUrl,
            ["source"] = post.Source,
            ["rating"] = post.Rating,
            ["score"] = post.Score,
            ["width"] = post.Width,
            ["height"] = post.Height,
            ["tags"] = post.TagList.ToList(),
            ["tagCategories"] = post.TagCategories.ToDictionary(e => e.Key, e => e.Value.ToString().ToLowerInvariant()),
        };
        return JsonSerializer.Serialize(data, Pretty) + "\n";
    }

    [GeneratedRegex(@"[\r\n]+")]
    private static partial Regex LineBreaks();
}
