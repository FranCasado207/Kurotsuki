namespace Kurotsuki.Core.Download.Comic;

public enum ArchiveKind { Zip, Tar, Rar, SevenZip }

/// <summary>Archive types a pool can be saved as (the <c>poolArchiveFormat</c> setting).</summary>
public sealed record ArchiveFormat(string Extension, ArchiveKind Kind)
{
    public static readonly ArchiveFormat Cbz = new("cbz", ArchiveKind.Zip);
    public static readonly IReadOnlyList<ArchiveFormat> All =
    [
        Cbz, new("zip", ArchiveKind.Zip), new("cbt", ArchiveKind.Tar), new("tar", ArchiveKind.Tar),
        new("cbr", ArchiveKind.Rar), new("rar", ArchiveKind.Rar), new("cb7", ArchiveKind.SevenZip), new("7z", ArchiveKind.SevenZip),
    ];

    /// <summary>Accepts "cbz", ".CBZ", " cbz " and so on.</summary>
    public static ArchiveFormat? Parse(string? value)
    {
        var cleaned = (value ?? "").Trim().ToLowerInvariant().TrimStart('.');
        return All.FirstOrDefault(f => f.Extension == cleaned);
    }

    /// <summary>Every listed format can be written (7z/cb7 uses the store-only "Copy" method).</summary>
    public bool CanWrite => true;
}
