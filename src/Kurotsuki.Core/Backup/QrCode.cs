using System.Text;

namespace Kurotsuki.Core.Backup;

/// <summary>
/// A small QR Code encoder: byte mode, error-correction level M, versions 1–10 (up to 213 bytes). Enough for the
/// transfer link (about 80 characters). Follows ISO/IEC 18004: Reed-Solomon over GF(256)/0x11D, the eight data masks
/// chosen by the standard penalty rules, BCH-protected format and version information.
/// </summary>
public static class QrCode
{
    private sealed record Spec(int Size, int[] Alignment, int EccPerBlock, int Blocks1, int Data1, int Blocks2, int Data2);

    // level M, versions 1..10 (index 0 = version 1)
    private static readonly Spec[] Specs =
    [
        new(21, [], 10, 1, 16, 0, 0),
        new(25, [6, 18], 16, 1, 28, 0, 0),
        new(29, [6, 22], 26, 1, 44, 0, 0),
        new(33, [6, 26], 18, 2, 32, 0, 0),
        new(37, [6, 30], 24, 2, 43, 0, 0),
        new(41, [6, 34], 16, 4, 27, 0, 0),
        new(45, [6, 22, 38], 18, 4, 31, 0, 0),
        new(49, [6, 24, 42], 22, 2, 38, 2, 39),
        new(53, [6, 26, 46], 22, 3, 36, 2, 37),
        new(57, [6, 28, 50], 26, 4, 43, 1, 44),
    ];

    public const int MaxBytes = 213; // version 10-M: 346 codewords − overhead

    /// <summary>The module matrix (true = dark), without the quiet zone.</summary>
    public static bool[,] Encode(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var version = -1;
        for (var v = 0; v < Specs.Length; v++)
        {
            var dataCodewords = Specs[v].Blocks1 * Specs[v].Data1 + Specs[v].Blocks2 * Specs[v].Data2;
            var countBits = v + 1 >= 10 ? 16 : 8;
            if ((4 + countBits + 8 * bytes.Length + 7) / 8 <= dataCodewords)
            {
                version = v;
                break;
            }
        }
        if (version < 0)
        {
            throw new ArgumentException($"Text too long for this QR encoder ({bytes.Length} bytes, max {MaxBytes})");
        }
        var spec = Specs[version];
        var codewords = Interleave(spec, BuildData(spec, version + 1, bytes));

        var best = (Matrix: (bool[,]?)null, Score: int.MaxValue);
        for (var mask = 0; mask < 8; mask++)
        {
            var matrix = Build(spec, version + 1, codewords, mask);
            var score = Penalty(matrix);
            if (score < best.Score)
            {
                best = (matrix, score);
            }
        }
        return best.Matrix!;
    }

    // ---- data -----------------------------------------------------------------------------------------------

    private static byte[] BuildData(Spec spec, int version, byte[] data)
    {
        var capacity = spec.Blocks1 * spec.Data1 + spec.Blocks2 * spec.Data2;
        var bits = new List<bool>();
        void Add(int value, int count)
        {
            for (var i = count - 1; i >= 0; i--)
            {
                bits.Add(((value >> i) & 1) != 0);
            }
        }
        Add(0b0100, 4); // byte mode
        Add(data.Length, version >= 10 ? 16 : 8);
        foreach (var b in data)
        {
            Add(b, 8);
        }
        Add(0, Math.Min(4, capacity * 8 - bits.Count)); // terminator
        while (bits.Count % 8 != 0)
        {
            bits.Add(false);
        }
        var result = new List<byte>();
        for (var i = 0; i < bits.Count; i += 8)
        {
            var value = 0;
            for (var k = 0; k < 8; k++)
            {
                value = (value << 1) | (bits[i + k] ? 1 : 0);
            }
            result.Add((byte)value);
        }
        for (var pad = 0xEC; result.Count < capacity; pad ^= 0xEC ^ 0x11)
        {
            result.Add((byte)pad);
        }
        return [.. result];
    }

    private static byte[] Interleave(Spec spec, byte[] data)
    {
        var blocks = new List<(byte[] Data, byte[] Ecc)>();
        var pos = 0;
        var generator = Generator(spec.EccPerBlock);
        for (var i = 0; i < spec.Blocks1 + spec.Blocks2; i++)
        {
            var size = i < spec.Blocks1 ? spec.Data1 : spec.Data2;
            var block = data[pos..(pos + size)];
            pos += size;
            blocks.Add((block, ReedSolomon(block, generator)));
        }
        var output = new List<byte>();
        var maxData = Math.Max(spec.Data1, spec.Data2);
        for (var i = 0; i < maxData; i++)
        {
            foreach (var (d, _) in blocks)
            {
                if (i < d.Length)
                {
                    output.Add(d[i]);
                }
            }
        }
        for (var i = 0; i < spec.EccPerBlock; i++)
        {
            foreach (var (_, e) in blocks)
            {
                output.Add(e[i]);
            }
        }
        return [.. output];
    }

    // ---- Reed-Solomon over GF(256) --------------------------------------------------------------------------

    private static readonly byte[] Exp = new byte[512];
    private static readonly byte[] Log = new byte[256];

    static QrCode()
    {
        var x = 1;
        for (var i = 0; i < 255; i++)
        {
            Exp[i] = (byte)x;
            Log[x] = (byte)i;
            x <<= 1;
            if (x >= 256)
            {
                x ^= 0x11D;
            }
        }
        for (var i = 255; i < 512; i++)
        {
            Exp[i] = Exp[i - 255];
        }
    }

    private static byte Multiply(int a, int b) => a == 0 || b == 0 ? (byte)0 : Exp[Log[a] + Log[b]];

    private static byte[] Generator(int degree)
    {
        var poly = new byte[degree + 1];
        poly[0] = 1;
        for (var i = 0; i < degree; i++)
        {
            for (var j = degree; j >= 0; j--)
            {
                poly[j] = (byte)(Multiply(poly[j], Exp[i]) ^ (j > 0 ? poly[j - 1] : 0));
            }
        }
        return poly; // poly[0] is the coefficient of x^degree ... reversed below by index use
    }

    private static byte[] ReedSolomon(byte[] data, byte[] generator)
    {
        // generator holds coefficients from x^0 upward; the division works on the highest degree first
        var degree = generator.Length - 1;
        var remainder = new byte[degree];
        foreach (var b in data)
        {
            var factor = (byte)(b ^ remainder[0]);
            Array.Copy(remainder, 1, remainder, 0, degree - 1);
            remainder[degree - 1] = 0;
            for (var i = 0; i < degree; i++)
            {
                remainder[i] ^= Multiply(generator[degree - 1 - i], factor);
            }
        }
        return remainder;
    }

    // ---- matrix ---------------------------------------------------------------------------------------------

    private static bool[,] Build(Spec spec, int version, byte[] codewords, int mask)
    {
        var n = spec.Size;
        var m = new bool[n, n];
        var reserved = new bool[n, n];

        void Set(int r, int c, bool dark, bool function = true)
        {
            if (r < 0 || c < 0 || r >= n || c >= n)
            {
                return;
            }
            m[r, c] = dark;
            if (function)
            {
                reserved[r, c] = true;
            }
        }

        // finder patterns with separators
        foreach (var (fr, fc) in new[] { (0, 0), (0, n - 7), (n - 7, 0) })
        {
            for (var dr = -1; dr <= 7; dr++)
            {
                for (var dc = -1; dc <= 7; dc++)
                {
                    var inside = dr is >= 0 and <= 6 && dc is >= 0 and <= 6;
                    var dark = inside && (dr is 0 or 6 || dc is 0 or 6 || (dr is >= 2 and <= 4 && dc is >= 2 and <= 4));
                    Set(fr + dr, fc + dc, dark);
                }
            }
        }
        // timing patterns
        for (var i = 8; i < n - 8; i++)
        {
            Set(6, i, i % 2 == 0);
            Set(i, 6, i % 2 == 0);
        }
        // alignment patterns
        var last = spec.Alignment.Length > 0 ? spec.Alignment[^1] : 0;
        foreach (var r in spec.Alignment)
        {
            foreach (var c in spec.Alignment)
            {
                // The three that would sit on a finder pattern are left out; those on the timing lines are real.
                if ((r == 6 && c == 6) || (r == 6 && c == last) || (r == last && c == 6))
                {
                    continue;
                }
                for (var dr = -2; dr <= 2; dr++)
                {
                    for (var dc = -2; dc <= 2; dc++)
                    {
                        Set(r + dr, c + dc, Math.Max(Math.Abs(dr), Math.Abs(dc)) != 1);
                    }
                }
            }
        }
        Set(n - 8, 8, true); // dark module
        // reserve format areas
        for (var i = 0; i < 9; i++)
        {
            reserved[8, i] = true;
            reserved[i, 8] = true;
        }
        for (var i = 0; i < 8; i++)
        {
            reserved[8, n - 1 - i] = true;
            reserved[n - 1 - i, 8] = true;
        }
        // version information
        if (version >= 7)
        {
            var info = VersionInfo(version);
            for (var i = 0; i < 18; i++)
            {
                var bit = ((info >> i) & 1) != 0;
                Set(i / 3, n - 11 + i % 3, bit);
                Set(n - 11 + i % 3, i / 3, bit);
            }
        }

        // data, zig-zag from the bottom right
        var bitIndex = 0;
        var upward = true;
        for (var col = n - 1; col > 0; col -= 2)
        {
            if (col == 6)
            {
                col--;
            }
            for (var step = 0; step < n; step++)
            {
                var row = upward ? n - 1 - step : step;
                for (var k = 0; k < 2; k++)
                {
                    var c = col - k;
                    if (reserved[row, c])
                    {
                        continue;
                    }
                    var dark = bitIndex < codewords.Length * 8 && ((codewords[bitIndex >> 3] >> (7 - (bitIndex & 7))) & 1) != 0;
                    bitIndex++;
                    m[row, c] = dark ^ MaskBit(mask, row, c);
                }
            }
            upward = !upward;
        }

        // format information (level M = 00)
        var format = FormatInfo(mask);
        for (var i = 0; i < 15; i++)
        {
            var bit = ((format >> i) & 1) != 0;
            if (i < 6) m[i, 8] = bit; else if (i < 8) m[i + 1, 8] = bit; else m[n - 15 + i, 8] = bit;
            if (i < 8) m[8, n - 1 - i] = bit; else if (i < 9) m[8, 15 - i] = bit; else m[8, 14 - i] = bit;
        }
        m[n - 8, 8] = true;
        return m;
    }

    private static bool MaskBit(int mask, int r, int c) => mask switch
    {
        0 => (r + c) % 2 == 0,
        1 => r % 2 == 0,
        2 => c % 3 == 0,
        3 => (r + c) % 3 == 0,
        4 => (r / 2 + c / 3) % 2 == 0,
        5 => r * c % 2 + r * c % 3 == 0,
        6 => (r * c % 2 + r * c % 3) % 2 == 0,
        _ => ((r + c) % 2 + r * c % 3) % 2 == 0,
    };

    private static int FormatInfo(int mask)
    {
        var data = (0b00 << 3) | mask; // error correction level M = 00
        var rem = data;
        for (var i = 0; i < 10; i++)
        {
            rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        }
        return ((data << 10) | rem) ^ 0x5412;
    }

    private static int VersionInfo(int version)
    {
        var rem = version;
        for (var i = 0; i < 12; i++)
        {
            rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
        }
        return (version << 12) | rem;
    }

    private static int Penalty(bool[,] m)
    {
        var n = m.GetLength(0);
        var score = 0;
        for (var pass = 0; pass < 2; pass++)
        {
            for (var a = 0; a < n; a++)
            {
                var run = 1;
                for (var b = 1; b < n; b++)
                {
                    var same = (pass == 0 ? m[a, b] == m[a, b - 1] : m[b, a] == m[b - 1, a]);
                    if (same) run++;
                    else { if (run >= 5) score += 3 + run - 5; run = 1; }
                }
                if (run >= 5) score += 3 + run - 5;
                // finder-like 1:1:3:1:1 patterns with 4 light modules on a side
                for (var b = 0; b + 10 < n; b++)
                {
                    bool Get(int i) => pass == 0 ? m[a, b + i] : m[b + i, a];
                    var pattern1 = Get(0) && !Get(1) && Get(2) && Get(3) && Get(4) && !Get(5) && Get(6) && !Get(7) && !Get(8) && !Get(9) && !Get(10);
                    var pattern2 = !Get(0) && !Get(1) && !Get(2) && !Get(3) && Get(4) && !Get(5) && Get(6) && Get(7) && Get(8) && !Get(9) && Get(10);
                    if (pattern1) score += 40;
                    if (pattern2) score += 40;
                }
            }
        }
        for (var r = 0; r + 1 < n; r++)
        {
            for (var c = 0; c + 1 < n; c++)
            {
                if (m[r, c] == m[r, c + 1] && m[r, c] == m[r + 1, c] && m[r, c] == m[r + 1, c + 1])
                {
                    score += 3;
                }
            }
        }
        var dark = 0;
        foreach (var cell in m)
        {
            if (cell) dark++;
        }
        var percent = dark * 100 / (n * n);
        score += Math.Abs(percent - 50) / 5 * 10;
        return score;
    }
}
