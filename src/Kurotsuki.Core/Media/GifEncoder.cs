namespace Kurotsuki.Core.Media;

/// <summary>
/// Writes an animated GIF (looping forever) from full-canvas RGBA frames. Each frame gets its own 256-colour palette
/// (median cut on a 5-bit-per-channel histogram), so photos from ugoira look reasonable without dithering.
/// </summary>
public static class GifEncoder
{
    /// <summary>Alpha below this is written as the transparent colour.</summary>
    private const int AlphaCutoff = 128;

    public static void Write(Stream output, IEnumerable<AnimationFrame> frames)
    {
        var started = false;
        foreach (var frame in frames)
        {
            if (!started)
            {
                WriteHeader(output, frame.Width, frame.Height);
                started = true;
            }
            WriteFrame(output, frame);
        }
        if (!started)
        {
            throw new InvalidOperationException("A GIF needs at least one frame");
        }
        output.WriteByte(0x3B);
    }

    private static void WriteHeader(Stream o, int width, int height)
    {
        o.Write("GIF89a"u8);
        WriteShort(o, width);
        WriteShort(o, height);
        o.WriteByte(0x70); // no global table, 8 bits of colour resolution
        o.WriteByte(0);
        o.WriteByte(0);
        // NETSCAPE2.0: loop forever
        o.Write(new byte[] { 0x21, 0xFF, 0x0B });
        o.Write("NETSCAPE2.0"u8);
        o.Write(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });
    }

    private static void WriteFrame(Stream o, AnimationFrame frame)
    {
        var (palette, indices, transparent) = Quantize(frame.Rgba, frame.Width * frame.Height);
        var delay = (int)Math.Max(2, Math.Round(frame.Delay.TotalMilliseconds / 10.0));

        o.Write(new byte[] { 0x21, 0xF9, 0x04, (byte)(transparent >= 0 ? 0x09 : 0x04) }); // disposal "do not dispose"
        WriteShort(o, delay);
        o.WriteByte((byte)Math.Max(0, transparent));
        o.WriteByte(0);

        o.WriteByte(0x2C);
        WriteShort(o, 0);
        WriteShort(o, 0);
        WriteShort(o, frame.Width);
        WriteShort(o, frame.Height);
        o.WriteByte(0x87); // local colour table of 256 entries
        o.Write(palette);

        o.WriteByte(8);
        var compressed = LzwEncode(8, indices);
        for (var i = 0; i < compressed.Length; i += 255)
        {
            var size = Math.Min(255, compressed.Length - i);
            o.WriteByte((byte)size);
            o.Write(compressed, i, size);
        }
        o.WriteByte(0);
    }

    private static void WriteShort(Stream o, int value)
    {
        o.WriteByte((byte)value);
        o.WriteByte((byte)(value >> 8));
    }

    // ---- quantisation ---------------------------------------------------------------------------------------

    private sealed class Box(int[] bins, int start, int count)
    {
        public int Start = start, Count = count;
        public int Min(int channel) { var m = 31; for (var i = Start; i < Start + Count; i++) m = Math.Min(m, Channel(bins[i], channel)); return m; }
        public int Max(int channel) { var m = 0; for (var i = Start; i < Start + Count; i++) m = Math.Max(m, Channel(bins[i], channel)); return m; }
    }

    private static int Channel(int bin, int channel) => (bin >> (10 - 5 * channel)) & 31; // 0 = r, 1 = g, 2 = b

    private static (byte[] Palette, byte[] Indices, int Transparent) Quantize(byte[] rgba, int pixels)
    {
        var histogram = new int[32768];
        var hasTransparent = false;
        for (var i = 0; i < pixels; i++)
        {
            if (rgba[i * 4 + 3] < AlphaCutoff)
            {
                hasTransparent = true;
                continue;
            }
            histogram[(rgba[i * 4] >> 3 << 10) | (rgba[i * 4 + 1] >> 3 << 5) | (rgba[i * 4 + 2] >> 3)]++;
        }
        var bins = Enumerable.Range(0, 32768).Where(b => histogram[b] > 0).ToArray();
        var colors = hasTransparent ? 255 : 256;
        var boxes = new List<Box> { new(bins, 0, bins.Length) };
        while (boxes.Count < colors)
        {
            // split the box with the widest range (by population) along its longest channel
            Box? best = null;
            var bestScore = 0L;
            var bestChannel = 0;
            foreach (var box in boxes.Where(b => b.Count > 1))
            {
                for (var c = 0; c < 3; c++)
                {
                    var range = box.Max(c) - box.Min(c);
                    var score = (long)range * 1000 + Math.Min(box.Count, 999);
                    if (range > 0 && score > bestScore)
                    {
                        (best, bestScore, bestChannel) = (box, score, c);
                    }
                }
            }
            if (best is null)
            {
                break;
            }
            Array.Sort(bins, best.Start, best.Count, Comparer<int>.Create((a, b) => Channel(a, bestChannel).CompareTo(Channel(b, bestChannel))));
            // median by pixel population
            long total = 0;
            for (var i = best.Start; i < best.Start + best.Count; i++) total += histogram[bins[i]];
            long running = 0;
            var split = best.Start + 1;
            for (var i = best.Start; i < best.Start + best.Count - 1; i++)
            {
                running += histogram[bins[i]];
                split = i + 1;
                if (running * 2 >= total) break;
            }
            var right = new Box(bins, split, best.Start + best.Count - split);
            best.Count = split - best.Start;
            boxes.Add(right);
        }

        var palette = new byte[256 * 3];
        var binToIndex = new byte[32768];
        var offset = hasTransparent ? 1 : 0;
        for (var b = 0; b < boxes.Count; b++)
        {
            long r = 0, g = 0, bl = 0, n = 0;
            for (var i = boxes[b].Start; i < boxes[b].Start + boxes[b].Count; i++)
            {
                var weight = histogram[bins[i]];
                r += (long)Channel(bins[i], 0) * weight;
                g += (long)Channel(bins[i], 1) * weight;
                bl += (long)Channel(bins[i], 2) * weight;
                n += weight;
            }
            var index = b + offset;
            if (n > 0)
            {
                palette[index * 3] = Expand(r / (double)n);
                palette[index * 3 + 1] = Expand(g / (double)n);
                palette[index * 3 + 2] = Expand(bl / (double)n);
            }
            for (var i = boxes[b].Start; i < boxes[b].Start + boxes[b].Count; i++)
            {
                binToIndex[bins[i]] = (byte)index;
            }
        }

        var indices = new byte[pixels];
        for (var i = 0; i < pixels; i++)
        {
            indices[i] = rgba[i * 4 + 3] < AlphaCutoff
                ? (byte)0
                : binToIndex[(rgba[i * 4] >> 3 << 10) | (rgba[i * 4 + 1] >> 3 << 5) | (rgba[i * 4 + 2] >> 3)];
        }
        return (palette, indices, hasTransparent ? 0 : -1);
    }

    /// <summary>Mean of 5-bit values back to 8 bits (replicating the top bits gives 0 → 0 and 31 → 255).</summary>
    private static byte Expand(double fiveBit)
    {
        var v = (int)Math.Round(fiveBit);
        return (byte)((v << 3) | (v >> 2));
    }

    // ---- LZW ------------------------------------------------------------------------------------------------

    internal static byte[] LzwEncode(int minCodeSize, byte[] indices)
    {
        var clear = 1 << minCodeSize;
        var end = clear + 1;
        var output = new MemoryStream();
        var buffer = 0;
        var bitCount = 0;
        var codeSize = minCodeSize + 1;

        void Emit(int code)
        {
            buffer |= code << bitCount;
            bitCount += codeSize;
            while (bitCount >= 8)
            {
                output.WriteByte((byte)buffer);
                buffer >>= 8;
                bitCount -= 8;
            }
        }

        var table = new Dictionary<int, int>();
        var next = end + 1;
        Emit(clear);
        int current = indices[0];
        for (var i = 1; i < indices.Length; i++)
        {
            var symbol = indices[i];
            var key = (current << 8) | symbol;
            if (table.TryGetValue(key, out var found))
            {
                current = found;
                continue;
            }
            Emit(current);
            if (next < 4096)
            {
                table[key] = next++;
                if (next > (1 << codeSize) && codeSize < 12)
                {
                    codeSize++;
                }
            }
            else
            {
                Emit(clear);
                table.Clear();
                next = end + 1;
                codeSize = minCodeSize + 1;
            }
            current = symbol;
        }
        Emit(current);
        Emit(end);
        if (bitCount > 0)
        {
            output.WriteByte((byte)buffer);
        }
        return output.ToArray();
    }
}
