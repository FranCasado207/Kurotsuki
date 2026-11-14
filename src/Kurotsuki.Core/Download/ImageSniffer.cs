namespace Kurotsuki.Core.Download;

/// <summary>Finds a file's real type from its first bytes (the server's extension can lie, or be missing).</summary>
public static class ImageSniffer
{
    public static string DetectExtension(string file, string fallback)
    {
        byte[] header = new byte[16];
        int read;
        try
        {
            using var stream = File.OpenRead(file);
            read = stream.Read(header);
        }
        catch (IOException)
        {
            return fallback;
        }
        bool At(int offset, params int[] bytes) =>
            read >= offset + bytes.Length && bytes.Select((b, i) => header[offset + i] == b).All(x => x);
        if (At(0, 0x89, 'P', 'N', 'G')) return "png";
        if (At(0, 0xFF, 0xD8, 0xFF)) return "jpg";
        if (At(0, 'G', 'I', 'F', '8')) return "gif";
        if (At(0, 'B', 'M')) return "bmp";
        if (At(0, 'R', 'I', 'F', 'F') && At(8, 'W', 'E', 'B', 'P')) return "webp";
        if (At(4, 'f', 't', 'y', 'p')) return "mp4";
        if (At(0, 0x1A, 0x45, 0xDF, 0xA3)) return "webm";
        if (At(0, 'P', 'K', 3, 4)) return "zip";
        return fallback;
    }
}
