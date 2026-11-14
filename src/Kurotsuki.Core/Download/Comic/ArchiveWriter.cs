using System.Formats.Tar;
using System.IO.Compression;

namespace Kurotsuki.Core.Download.Comic;

/// <summary>Writes the pages and ComicInfo.xml into the archive format the user chose.</summary>
public static class ArchiveWriter
{
    /// <summary>A file to store under <c>Name</c> (a plain file name, no folders).</summary>
    public sealed record Entry(string Name, string File);

    /// <summary>
    /// Writes the archive to <paramref name="target"/> (replacing it). Entries are stored in order after ComicInfo.xml,
    /// which is always first. Written to <c>.part</c> and renamed, so a crash never leaves a half archive under the real name.
    /// </summary>
    public static void Write(ArchiveFormat format, IReadOnlyList<Entry> entries, byte[] comicInfo, string target,
        IProgress<double>? progress = null)
    {
        var partial = target + ".part";
        try
        {
            using (var stream = new FileStream(partial, FileMode.Create, FileAccess.Write))
            {
                switch (format.Kind)
                {
                    case ArchiveKind.Zip: WriteZip(stream, entries, comicInfo, progress); break;
                    case ArchiveKind.Tar: WriteTar(stream, entries, comicInfo, progress); break;
                    case ArchiveKind.Rar: WriteRar(stream, entries, comicInfo, progress); break;
                    case ArchiveKind.SevenZip: WriteSevenZip(stream, entries, comicInfo, progress); break;
                    default: throw new NotSupportedException($"The .{format.Extension} format cannot be written.");
                }
            }
            File.Move(partial, target, overwrite: true);
        }
        finally
        {
            File.Delete(partial);
        }
    }

    private static void WriteZip(Stream stream, IReadOnlyList<Entry> entries, byte[] comicInfo, IProgress<double>? progress)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        var info = zip.CreateEntry(ComicInfo.FileName, CompressionLevel.Optimal);
        using (var s = info.Open())
        {
            s.Write(comicInfo);
        }
        for (var i = 0; i < entries.Count; i++)
        {
            // Pages are already compressed images, so deflating them again only costs time.
            var entry = zip.CreateEntry(entries[i].Name, CompressionLevel.NoCompression);
            using (var s = entry.Open())
            using (var f = File.OpenRead(entries[i].File))
            {
                f.CopyTo(s);
            }
            progress?.Report((i + 1.0) / entries.Count);
        }
    }

    private static void WriteTar(Stream stream, IReadOnlyList<Entry> entries, byte[] comicInfo, IProgress<double>? progress)
    {
        using var tar = new TarWriter(stream, TarEntryFormat.Ustar, leaveOpen: true);
        tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, ComicInfo.FileName) { DataStream = new MemoryStream(comicInfo) });
        for (var i = 0; i < entries.Count; i++)
        {
            using var f = File.OpenRead(entries[i].File);
            tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, entries[i].Name) { DataStream = f });
            progress?.Report((i + 1.0) / entries.Count);
        }
    }

    private static void WriteRar(Stream stream, IReadOnlyList<Entry> entries, byte[] comicInfo, IProgress<double>? progress)
    {
        using var rar = new RarWriter(stream);
        rar.Add(ComicInfo.FileName, comicInfo);
        for (var i = 0; i < entries.Count; i++)
        {
            rar.Add(entries[i].Name, entries[i].File);
            progress?.Report((i + 1.0) / entries.Count);
        }
    }

    private static void WriteSevenZip(Stream stream, IReadOnlyList<Entry> entries, byte[] comicInfo, IProgress<double>? progress)
    {
        var sevenZip = new SevenZipWriter(stream);
        sevenZip.Add(ComicInfo.FileName, comicInfo);
        for (var i = 0; i < entries.Count; i++)
        {
            sevenZip.Add(entries[i].Name, entries[i].File);
            progress?.Report((i + 1.0) / entries.Count);
        }
        sevenZip.Finish();
    }
}
