namespace Kurotsuki.Core.Download;

public enum SidecarFormat
{
    None,
    /// <summary><c>file.jpg.txt</c>: one tag per line, the convention Hydrus' "filename tag" importers read.</summary>
    Txt,
    /// <summary><c>file.jpg.json</c>: id, source, rating, score, size, url and the tags with their categories.</summary>
    Json,
}

public static class SidecarFormats
{
    public static SidecarFormat Parse(string? value) =>
        Enum.TryParse<SidecarFormat>(value?.Trim(), true, out var f) ? f : SidecarFormat.None;

    public static string Suffix(this SidecarFormat format) => format switch
    {
        SidecarFormat.Txt => ".txt",
        SidecarFormat.Json => ".json",
        _ => "",
    };
}

/// <summary>What the user configured for saving files; read fresh each time a download starts.</summary>
public sealed record DownloadSettings(string? Folder, string? Template, SidecarFormat Sidecar, bool SkipKnown)
{
    public bool HasFolder => !string.IsNullOrWhiteSpace(Folder);
}
