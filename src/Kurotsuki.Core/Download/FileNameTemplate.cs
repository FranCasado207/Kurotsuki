using System.Text.RegularExpressions;
using Kurotsuki.Core.Booru;

namespace Kurotsuki.Core.Download;

/// <summary>
/// Turns a template such as <c>%booru%/%artist%/%id%_%md5%.%ext%</c> into a relative path for a post. Every
/// substituted value is sanitised on its own, so a hostile tag or file name can neither add path separators nor
/// climb out of the download folder; only the <c>/</c> typed in the template itself separates folders.
/// </summary>
public sealed partial class FileNameTemplate
{
    public const string Default = "%file%";
    public const string Tokens = "%booru% %artist% %group% %id% %md5% %file% %name% %ext% %rating% %score%";
    private const int MaxNameLength = 200;

    private readonly string _template;
    private readonly Func<Post, string> _groupOf;

    public FileNameTemplate(string? template, Func<Post, string>? groupOf = null)
    {
        _template = string.IsNullOrWhiteSpace(template) ? Default : template.Trim();
        _groupOf = groupOf ?? (_ => "");
    }

    /// <summary>The relative path (forward slashes); never empty, never absolute, never contains "..".</summary>
    public string Render(Post post)
    {
        var original = SafeFileName(post);
        var segments = new List<string>();
        foreach (var part in PathSeparators().Split(_template))
        {
            var value = Sanitize(Substitute(part, post, original), "");
            if (value.Length > 0)
            {
                segments.Add(value);
            }
        }
        if (segments.Count == 0)
        {
            return original;
        }
        // A template without %ext% or %file% would otherwise produce files with no extension.
        var last = segments[^1];
        if (!last.Contains('.') && !_template.Contains("%ext%") && !_template.Contains("%file%"))
        {
            var ext = ExtensionOf(original);
            if (ext.Length > 0)
            {
                segments[^1] = last + "." + ext;
            }
        }
        return string.Join('/', segments);
    }

    private string Substitute(string part, Post post, string original) =>
        Token().Replace(part, m => Clean(ValueOf(m.Groups[1].Value, post, original)));

    private string ValueOf(string token, Post post, string original) => token switch
    {
        "booru" => HostOf(post.BooruBaseUrl),
        "artist" => ArtistOf(post),
        "group" => _groupOf(post),
        "id" => post.Id.ToString(),
        "md5" => Md5Of(original, post),
        "file" => original,
        "name" => StemOf(original),
        "ext" => ExtensionOf(original),
        "rating" => post.Rating ?? "",
        "score" => post.Score.ToString(),
        _ => $"%{token}%",
    };

    private static string Clean(string? value) => value is null ? "" : value.Replace('/', '_').Replace('\\', '_');

    internal static string ArtistOf(Post post) =>
        post.TagCategories.FirstOrDefault(e => e.Value == TagCategory.Artist).Key ?? "unknown";

    private static string Md5Of(string original, Post post)
    {
        var stem = StemOf(original);
        return Md5().IsMatch(stem) ? stem.ToLowerInvariant() : post.Id.ToString();
    }

    internal static string StemOf(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }

    internal static string ExtensionOf(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot > 0 && dot < name.Length - 1 ? name[(dot + 1)..] : "";
    }

    internal static string HostOf(string? baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ? uri.Host : "";

    /// <summary>Builds a file name from server-controlled data so it can never point outside the download folder.</summary>
    public static string SafeFileName(Post post)
    {
        var name = LastSegment(post.Image);
        if (name.Length == 0)
        {
            name = LastSegment(PathOf(post.FileUrl));
        }
        return Sanitize(name, $"post-{post.Id}");
    }

    /// <summary>Makes a remote-supplied name safe as a single file name: no separators, reserved characters or leading dots.</summary>
    public static string Sanitize(string? raw, string fallback)
    {
        var name = Reserved().Replace(LastSegment(raw), "_").Trim().TrimStart('.');
        if (name.Length > MaxNameLength)
        {
            name = name[^MaxNameLength..];
        }
        return name.Length == 0 ? fallback : name;
    }

    /// <summary>
    /// A free path for the relative name (which may contain sub-folders) that is guaranteed to stay inside
    /// <paramref name="directory"/>; a number is added when the file exists or is in <paramref name="reserved"/>.
    /// </summary>
    public static string UniqueTarget(string directory, string relative, ISet<string>? reserved = null)
    {
        var baseDir = Path.GetFullPath(directory);
        var target = Path.GetFullPath(Path.Combine(baseDir, relative));
        var prefix = baseDir.EndsWith(Path.DirectorySeparatorChar) ? baseDir : baseDir + Path.DirectorySeparatorChar;
        if (!target.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new IOException($"Refusing to write outside the download folder: {relative}");
        }
        var fileName = Path.GetFileName(target);
        var dot = fileName.LastIndexOf('.');
        var stem = dot > 0 ? fileName[..dot] : fileName;
        var ext = dot > 0 ? fileName[dot..] : "";
        for (var n = 1; File.Exists(target) || reserved?.Contains(target) == true; n++)
        {
            target = Path.Combine(Path.GetDirectoryName(target)!, $"{stem} ({n}){ext}");
        }
        return target;
    }

    private static string LastSegment(string? value)
    {
        if (value is null)
        {
            return "";
        }
        return value[(Math.Max(value.LastIndexOf('/'), value.LastIndexOf('\\')) + 1)..];
    }

    private static string PathOf(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? Uri.UnescapeDataString(uri.AbsolutePath) : "";

    [GeneratedRegex("[/\\\\]")] private static partial Regex PathSeparators();
    [GeneratedRegex("%([a-z0-9]+)%")] private static partial Regex Token();
    [GeneratedRegex("^[0-9a-fA-F]{32}$")] private static partial Regex Md5();
    [GeneratedRegex("[\\p{Cc}<>:\"|?*]")] private static partial Regex Reserved();
}
