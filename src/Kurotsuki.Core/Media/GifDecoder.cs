namespace Kurotsuki.Core.Media;

/// <summary>
/// A streaming GIF decoder: frames come out one at a time, composited on the logical screen with the file's disposal
/// rules, so a long animation never has to be held in memory. Delays below 20 ms are raised to 20 ms (as in the Java app).
/// </summary>
public static class GifDecoder
{
    public const int MaxPixels = 16_000_000;
    private static readonly TimeSpan MinDelay = TimeSpan.FromMilliseconds(20);

    public static bool LooksLikeGif(ReadOnlySpan<byte> header) =>
        header.Length >= 6 && header[0] == 'G' && header[1] == 'I' && header[2] == 'F' && header[3] == '8';

    /// <summary>Reads the logical screen size without decoding any frame.</summary>
    public static (int Width, int Height) ReadSize(string path)
    {
        using var stream = File.OpenRead(path);
        var header = new byte[10];
        stream.ReadExactly(header);
        if (!LooksLikeGif(header))
        {
            throw new InvalidDataException("Not a GIF");
        }
        return (header[6] | header[7] << 8, header[8] | header[9] << 8);
    }

    public static IEnumerable<AnimationFrame> Frames(string path)
    {
        using var stream = new BufferedStream(File.OpenRead(path), 64 * 1024);
        foreach (var frame in Frames(stream))
        {
            yield return frame;
        }
    }

    public static IEnumerable<AnimationFrame> Frames(Stream stream)
    {
        var header = new byte[13];
        stream.ReadExactly(header);
        if (!LooksLikeGif(header))
        {
            throw new InvalidDataException("Not a GIF");
        }
        int width = header[6] | header[7] << 8, height = header[8] | header[9] << 8;
        if (width == 0 || height == 0 || (long)width * height > MaxPixels)
        {
            throw new InvalidDataException($"Unsupported GIF size {width}x{height}");
        }
        var flags = header[10];
        byte[]? globalTable = (flags & 0x80) != 0 ? ReadTable(stream, 1 << ((flags & 7) + 1)) : null;

        var canvas = new byte[width * height * 4];
        byte[]? beforeFrame = null;
        // state of the pending graphic control extension
        var delay = TimeSpan.Zero;
        var transparent = -1;
        var disposal = 0;
        var previous = (Disposal: 0, Left: 0, Top: 0, W: 0, H: 0);

        while (true)
        {
            var block = stream.ReadByte();
            if (block is < 0 or 0x3B)
            {
                yield break;
            }
            if (block == 0x21)
            {
                var label = stream.ReadByte();
                if (label == 0xF9)
                {
                    var data = ReadSubBlocks(stream);
                    if (data.Length >= 4)
                    {
                        disposal = (data[0] >> 2) & 7;
                        transparent = (data[0] & 1) != 0 ? data[3] : -1;
                        delay = TimeSpan.FromMilliseconds((data[1] | data[2] << 8) * 10);
                    }
                }
                else
                {
                    SkipSubBlocks(stream);
                }
                continue;
            }
            if (block != 0x2C)
            {
                throw new InvalidDataException("Unexpected GIF block");
            }

            var desc = new byte[9];
            stream.ReadExactly(desc);
            int left = desc[0] | desc[1] << 8, top = desc[2] | desc[3] << 8, w = desc[4] | desc[5] << 8, h = desc[6] | desc[7] << 8;
            var local = desc[8];
            var table = (local & 0x80) != 0 ? ReadTable(stream, 1 << ((local & 7) + 1)) : globalTable
                ?? throw new InvalidDataException("GIF frame without a color table");
            var interlaced = (local & 0x40) != 0;
            var minCode = stream.ReadByte();
            var compressed = ReadSubBlocks(stream);
            if (minCode is < 2 or > 11 || (long)w * h > MaxPixels)
            {
                throw new InvalidDataException("Bad GIF frame");
            }
            var indices = LzwDecode(minCode, compressed, w * h);

            // dispose of the previous frame first
            switch (previous.Disposal)
            {
                case 2: ClearRect(canvas, width, height, previous.Left, previous.Top, previous.W, previous.H); break;
                case 3 when beforeFrame is not null: Buffer.BlockCopy(beforeFrame, 0, canvas, 0, canvas.Length); break;
            }
            if (disposal == 3)
            {
                beforeFrame ??= new byte[canvas.Length];
                Buffer.BlockCopy(canvas, 0, beforeFrame, 0, canvas.Length);
            }

            Paint(canvas, width, height, indices, table, transparent, left, top, w, h, interlaced);
            var emitted = new byte[canvas.Length];
            Buffer.BlockCopy(canvas, 0, emitted, 0, canvas.Length);
            yield return new AnimationFrame(width, height, emitted, delay < MinDelay ? MinDelay : delay);

            previous = (disposal, left, top, w, h);
            delay = TimeSpan.Zero;
            transparent = -1;
            disposal = 0;
        }
    }

    private static void Paint(byte[] canvas, int width, int height, byte[] indices, byte[] table, int transparent,
        int left, int top, int w, int h, bool interlaced)
    {
        for (var row = 0; row < h; row++)
        {
            var y = top + (interlaced ? InterlacedRow(row, h) : row);
            if (y >= height)
            {
                continue;
            }
            for (var col = 0; col < w; col++)
            {
                var x = left + col;
                var index = indices[row * w + col];
                if (x >= width || index == transparent)
                {
                    continue;
                }
                var at = (y * width + x) * 4;
                var t = index * 3;
                if (t + 2 >= table.Length)
                {
                    continue;
                }
                canvas[at] = table[t];
                canvas[at + 1] = table[t + 1];
                canvas[at + 2] = table[t + 2];
                canvas[at + 3] = 255;
            }
        }
    }

    /// <summary>Output row of the n-th stored row of an interlaced image (passes of 8/8/4/2).</summary>
    private static int InterlacedRow(int stored, int height)
    {
        var pass1 = (height + 7) / 8;
        var pass2 = (height + 3) / 8;
        var pass3 = (height + 1) / 4;
        if (stored < pass1) return stored * 8;
        stored -= pass1;
        if (stored < pass2) return stored * 8 + 4;
        stored -= pass2;
        if (stored < pass3) return stored * 4 + 2;
        return (stored - pass3) * 2 + 1;
    }

    private static void ClearRect(byte[] canvas, int width, int height, int left, int top, int w, int h)
    {
        for (var y = top; y < Math.Min(height, top + h); y++)
        {
            Array.Clear(canvas, (y * width + left) * 4, Math.Max(0, Math.Min(width - left, w)) * 4);
        }
    }

    private static byte[] ReadTable(Stream stream, int colors)
    {
        var table = new byte[colors * 3];
        stream.ReadExactly(table);
        return table;
    }

    private static byte[] ReadSubBlocks(Stream stream)
    {
        using var data = new MemoryStream();
        while (true)
        {
            var size = stream.ReadByte();
            if (size <= 0)
            {
                return data.ToArray();
            }
            var buffer = new byte[size];
            stream.ReadExactly(buffer);
            data.Write(buffer);
            if (data.Length > 64 * 1024 * 1024)
            {
                throw new InvalidDataException("GIF data too large");
            }
        }
    }

    private static void SkipSubBlocks(Stream stream)
    {
        int size;
        while ((size = stream.ReadByte()) > 0)
        {
            stream.Seek(size, SeekOrigin.Current);
        }
    }

    /// <summary>Standard GIF variable-width LZW. Missing pixels (truncated data) stay index 0.</summary>
    internal static byte[] LzwDecode(int minCodeSize, byte[] data, int pixelCount)
    {
        var output = new byte[pixelCount];
        var clear = 1 << minCodeSize;
        var end = clear + 1;
        var prefix = new short[4096];
        var suffix = new byte[4096];
        var stack = new byte[4097];
        for (var i = 0; i < clear; i++)
        {
            suffix[i] = (byte)i;
        }
        var codeSize = minCodeSize + 1;
        var next = end + 1;
        var previous = -1;
        var first = (byte)0;
        var bits = 0;
        var bitCount = 0;
        var pos = 0;
        var written = 0;

        while (written < pixelCount)
        {
            while (bitCount < codeSize)
            {
                if (pos >= data.Length)
                {
                    return output;
                }
                bits |= data[pos++] << bitCount;
                bitCount += 8;
            }
            var code = bits & ((1 << codeSize) - 1);
            bits >>= codeSize;
            bitCount -= codeSize;

            if (code == clear)
            {
                codeSize = minCodeSize + 1;
                next = end + 1;
                previous = -1;
                continue;
            }
            if (code == end)
            {
                break;
            }
            var current = code;
            var top = 0;
            if (previous < 0)
            {
                output[written++] = suffix[code];
                previous = code;
                first = suffix[code];
                continue;
            }
            if (code >= next)
            {
                if (code > next)
                {
                    throw new InvalidDataException("Corrupt GIF data");
                }
                stack[top++] = first;
                code = previous;
            }
            while (code >= clear)
            {
                stack[top++] = suffix[code];
                code = prefix[code];
            }
            first = suffix[code];
            stack[top++] = first;
            if (next < 4096)
            {
                prefix[next] = (short)previous;
                suffix[next] = first;
                next++;
                if (next == 1 << codeSize && codeSize < 12)
                {
                    codeSize++;
                }
            }
            previous = current;
            while (top > 0 && written < pixelCount)
            {
                output[written++] = stack[--top];
            }
        }
        return output;
    }
}
