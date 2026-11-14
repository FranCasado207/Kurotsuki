using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Kurotsuki.Core.Media;

/// <summary>
/// Pixiv "ugoira": a zip of still frames plus <c>animation.json</c> with each frame's delay. Same limits as the Java app,
/// so a hostile archive cannot exhaust memory.
/// </summary>
public static class Ugoira
{
    public const int DefaultDelayMs = 100;
    public const int MaxFrames = 2000;
    public const long MaxFrameBytes = 32L << 20;
    public const long MaxTotalBytes = 256L << 20;
    public const long MaxFramePixels = 64_000_000;
    private const long MaxMetadataBytes = 1 << 20;
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp"];

    public sealed record Frame(string Name, byte[] Data, int DelayMs);

    public static bool IsUgoiraZip(string file) => file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    public static string GifNameFor(string zip) => Path.ChangeExtension(zip, ".gif");

    /// <summary>The compressed frames in playback order (they stay encoded: decoding is done when a frame is shown).</summary>
    public static List<Frame> Read(string zipFile)
    {
        using var zip = ZipFile.OpenRead(zipFile);
        var order = FrameOrder(zip);
        if (order.Count > MaxFrames)
        {
            throw new IOException($"Ugoira has too many frames ({order.Count})");
        }
        var frames = new List<Frame>();
        long total = 0;
        foreach (var (name, delay) in order)
        {
            var entry = zip.GetEntry(name);
            if (entry is null || entry.FullName.EndsWith('/'))
            {
                continue;
            }
            // Sizes in the header can lie, so the limit is enforced while reading.
            var data = ReadBounded(entry, MaxFrameBytes);
            total += data.Length;
            if (total > MaxTotalBytes)
            {
                throw new IOException("Ugoira is too large when unpacked");
            }
            frames.Add(new Frame(name, data, delay));
        }
        if (frames.Count == 0)
        {
            throw new IOException("ZIP without image frames");
        }
        return frames;
    }

    /// <summary>Decodes the frames lazily, in order, one canvas per frame. Frames that cannot be decoded raise <see cref="IOException"/>.</summary>
    public static IEnumerable<AnimationFrame> Frames(IReadOnlyList<Frame> frames, IFrameDecoder decoder)
    {
        foreach (var frame in frames)
        {
            var image = decoder.Decode(frame.Data) ?? throw new IOException($"Unreadable ugoira frame {frame.Name}");
            if ((long)image.Width * image.Height > MaxFramePixels)
            {
                throw new IOException($"Ugoira frame has too many pixels: {frame.Name}");
            }
            yield return new AnimationFrame(image.Width, image.Height, image.Rgba, TimeSpan.FromMilliseconds(frame.DelayMs));
        }
    }

    /// <summary>Converts an ugoira zip into an animated GIF (written to a temporary file and renamed).</summary>
    public static string ConvertToGif(string zip, string gif, IFrameDecoder decoder)
    {
        var frames = Read(zip);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(gif))!);
        var partial = gif + ".part";
        try
        {
            using (var output = File.Create(partial))
            {
                int? width = null, height = null;
                GifEncoder.Write(output, Frames(frames, decoder).Select(f =>
                {
                    width ??= f.Width;
                    height ??= f.Height;
                    return f.Width == width && f.Height == height ? f : throw new IOException("Ugoira frames differ in size");
                }));
            }
            File.Move(partial, gif, overwrite: true);
        }
        finally
        {
            File.Delete(partial);
        }
        return gif;
    }

    private static List<(string Name, int Delay)> FrameOrder(ZipArchive zip)
    {
        var order = new List<(string, int)>();
        if (zip.GetEntry("animation.json") is { } metadata)
        {
            try
            {
                using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(ReadBounded(metadata, MaxMetadataBytes)));
                foreach (var frame in doc.RootElement.GetProperty("frames").EnumerateArray())
                {
                    order.Add((frame.GetProperty("file").GetString()!, frame.GetProperty("delay").GetInt32()));
                }
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            {
                order.Clear();
            }
        }
        if (order.Count == 0)
        {
            order.AddRange(zip.Entries
                .Where(e => !e.FullName.EndsWith('/') && ImageExtensions.Any(x => e.FullName.EndsWith(x, StringComparison.OrdinalIgnoreCase)))
                .Select(e => e.FullName).Order(StringComparer.Ordinal).Select(n => (n, DefaultDelayMs)));
        }
        return order;
    }

    private static byte[] ReadBounded(ZipArchiveEntry entry, long limit)
    {
        using var input = entry.Open();
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        int read;
        while ((read = input.Read(chunk)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > limit)
            {
                throw new IOException($"Ugoira entry too large: {entry.FullName}");
            }
        }
        return buffer.ToArray();
    }
}
