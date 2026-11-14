using System.Buffers.Binary;
using Kurotsuki.Core.Download.Comic;

namespace Kurotsuki.Core.Media;

/// <summary>
/// Animated PNG. Skia's PNG codec in the SkiaSharp build we use shows only the default image, so the animation is taken apart here:
/// each frame's data becomes a small ordinary PNG that the app's <see cref="IFrameDecoder"/> decodes, and the frames are composed on a
/// canvas following the APNG dispose and blend rules.
/// </summary>
public static class ApngDecoder
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private const int MinDelayMs = 20;
    private const int DefaultDelayMs = 100;

    private sealed record Chunk(string Type, byte[] Data);

    private sealed record FrameInfo(int Width, int Height, int X, int Y, int DelayMs, int Dispose, int Blend, List<byte[]> Data);

    /// <summary>True for a PNG whose animation control chunk announces more than one frame. Reads only the start of the file.</summary>
    public static bool IsAnimated(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var header = new byte[8];
            if (stream.Read(header) < 8 || !header.AsSpan().SequenceEqual(Signature))
            {
                return false;
            }
            foreach (var chunk in ReadChunks(stream))
            {
                if (chunk.Type == "acTL")
                {
                    return chunk.Data.Length >= 8 && BinaryPrimitives.ReadUInt32BigEndian(chunk.Data) > 1;
                }
                if (chunk.Type is "IDAT" or "IEND")
                {
                    return false; // acTL must come before the image data
                }
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
        }
        return false;
    }

    /// <summary>Full-canvas frames in order, each a fresh array.</summary>
    public static IEnumerable<AnimationFrame> Frames(string path, IFrameDecoder decoder)
    {
        using var stream = File.OpenRead(path);
        var header = new byte[8];
        if (stream.Read(header) < 8 || !header.AsSpan().SequenceEqual(Signature))
        {
            throw new InvalidDataException("Not a PNG");
        }

        byte[]? ihdr = null;
        var shared = new List<Chunk>(); // PLTE, tRNS, colour space chunks: every frame needs them
        var frames = new List<FrameInfo>();
        FrameInfo? current = null;
        var defaultIsFrame = false;
        foreach (var chunk in ReadChunks(stream))
        {
            switch (chunk.Type)
            {
                case "IHDR": ihdr = chunk.Data; break;
                case "PLTE" or "tRNS" or "gAMA" or "sRGB" or "iCCP" or "cHRM": shared.Add(chunk); break;
                case "fcTL":
                    if (chunk.Data.Length < 26)
                    {
                        throw new InvalidDataException("Bad fcTL chunk");
                    }
                    var num = BinaryPrimitives.ReadUInt16BigEndian(chunk.Data.AsSpan(20));
                    var den = BinaryPrimitives.ReadUInt16BigEndian(chunk.Data.AsSpan(22));
                    var ms = (int)(1000.0 * num / (den == 0 ? 100 : den));
                    current = new FrameInfo((int)BinaryPrimitives.ReadUInt32BigEndian(chunk.Data.AsSpan(4)),
                        (int)BinaryPrimitives.ReadUInt32BigEndian(chunk.Data.AsSpan(8)),
                        (int)BinaryPrimitives.ReadUInt32BigEndian(chunk.Data.AsSpan(12)),
                        (int)BinaryPrimitives.ReadUInt32BigEndian(chunk.Data.AsSpan(16)),
                        ms < MinDelayMs ? DefaultDelayMs : ms, chunk.Data[24], chunk.Data[25], []);
                    frames.Add(current);
                    break;
                case "IDAT":
                    if (current is not null && frames.Count == 1 && !defaultIsFrame)
                    {
                        defaultIsFrame = true; // an fcTL before IDAT: the default image is the first frame
                    }
                    if (defaultIsFrame && frames.Count == 1)
                    {
                        current!.Data.Add(chunk.Data);
                    }
                    break;
                case "fdAT":
                    current?.Data.Add(chunk.Data[4..]); // drop the sequence number
                    break;
            }
        }
        if (ihdr is null || frames.Count == 0)
        {
            throw new InvalidDataException("No animation frames");
        }

        var canvasWidth = (int)BinaryPrimitives.ReadUInt32BigEndian(ihdr);
        var canvasHeight = (int)BinaryPrimitives.ReadUInt32BigEndian(ihdr.AsSpan(4));
        if ((long)canvasWidth * canvasHeight > GifDecoder.MaxPixels)
        {
            throw new InvalidDataException("Animation too large");
        }
        var canvas = new byte[canvasWidth * canvasHeight * 4];
        byte[]? beforePrevious = null;
        FrameInfo? previous = null;
        foreach (var frame in frames)
        {
            // What the previous frame asked to happen to its area once it had been shown.
            if (previous is not null)
            {
                if (previous.Dispose == 1)
                {
                    Clear(canvas, canvasWidth, previous);
                }
                else if (previous.Dispose == 2 && beforePrevious is not null)
                {
                    beforePrevious.CopyTo(canvas, 0);
                }
            }
            beforePrevious = frame.Dispose == 2 ? (byte[])canvas.Clone() : null;

            var decoded = decoder.Decode(BuildPng(ihdr, frame, shared))
                ?? throw new InvalidDataException("A frame could not be decoded");
            if (decoded.Width != frame.Width || decoded.Height != frame.Height
                || frame.X + frame.Width > canvasWidth || frame.Y + frame.Height > canvasHeight)
            {
                throw new InvalidDataException("Frame does not fit the canvas");
            }
            Draw(canvas, canvasWidth, frame, decoded.Rgba);
            previous = frame;
            yield return new AnimationFrame(canvasWidth, canvasHeight, (byte[])canvas.Clone(), TimeSpan.FromMilliseconds(frame.DelayMs));
        }
    }

    private static void Clear(byte[] canvas, int canvasWidth, FrameInfo frame)
    {
        for (var y = 0; y < frame.Height; y++)
        {
            Array.Clear(canvas, ((frame.Y + y) * canvasWidth + frame.X) * 4, frame.Width * 4);
        }
    }

    private static void Draw(byte[] canvas, int canvasWidth, FrameInfo frame, byte[] source)
    {
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                var s = (y * frame.Width + x) * 4;
                var d = ((frame.Y + y) * canvasWidth + frame.X + x) * 4;
                var sa = source[s + 3];
                if (frame.Blend == 0 || sa == 255 || canvas[d + 3] == 0)
                {
                    Buffer.BlockCopy(source, s, canvas, d, 4); // source mode, or nothing underneath to blend with
                    continue;
                }
                if (sa == 0)
                {
                    continue;
                }
                var da = canvas[d + 3];
                var sf = sa / 255.0;
                var df = da / 255.0 * (1 - sf);
                var outA = sf + df;
                for (var c = 0; c < 3; c++)
                {
                    canvas[d + c] = (byte)Math.Round((source[s + c] * sf + canvas[d + c] * df) / outA);
                }
                canvas[d + 3] = (byte)Math.Round(outA * 255);
            }
        }
    }

    private static byte[] BuildPng(byte[] ihdr, FrameInfo frame, List<Chunk> shared)
    {
        using var output = new MemoryStream();
        output.Write(Signature);
        var header = (byte[])ihdr.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)frame.Width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)frame.Height);
        WriteChunk(output, "IHDR", header);
        foreach (var chunk in shared)
        {
            WriteChunk(output, chunk.Type, chunk.Data);
        }
        foreach (var data in frame.Data)
        {
            WriteChunk(output, "IDAT", data);
        }
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var body = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type, body);
        data.CopyTo(body, 4);
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
        output.Write(number);
        output.Write(body);
        BinaryPrimitives.WriteUInt32BigEndian(number, RarCrc.Crc(body));
        output.Write(number);
    }

    private static IEnumerable<Chunk> ReadChunks(Stream stream)
    {
        var head = new byte[8];
        while (stream.Read(head) == 8)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(head);
            if (length > 256 * 1024 * 1024)
            {
                throw new InvalidDataException("Chunk too large");
            }
            var data = new byte[length];
            stream.ReadExactly(data);
            stream.ReadExactly(new byte[4]); // CRC: the decoder that finally reads the frame checks its own data
            yield return new Chunk(System.Text.Encoding.ASCII.GetString(head, 4, 4), data);
        }
    }
}
