using System.Runtime.InteropServices;
using Kurotsuki.Core.Media;
using SkiaSharp;

namespace Kurotsuki.App.Services;

/// <summary>
/// Frames of an animated WebP or APNG, composed by Skia's codec (blend and dispose modes included). GIF and ugoira have their own
/// decoders; this is for the formats Avalonia's <c>Bitmap</c> only shows the first frame of.
/// </summary>
public static class SkiaAnimation
{
    private const int MinDelayMs = 20;
    private const int DefaultDelayMs = 100;

    /// <summary>True when the file holds more than one frame. Reads only the header.</summary>
    public static bool IsAnimated(string path)
    {
        try
        {
            using var codec = SKCodec.Create(path);
            return codec is { FrameCount: > 1 };
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// The frames in order. Every frame reuses one buffer (the next call to MoveNext overwrites it), so a consumer must copy
    /// the pixels before asking for the next frame, as <see cref="AnimationPlayer"/> does.
    /// </summary>
    public static IEnumerable<AnimationFrame> Frames(string path)
    {
        using var codec = SKCodec.Create(path) ?? throw new InvalidDataException("Not an image Skia can read");
        var infos = codec.FrameInfo;
        var count = Math.Max(1, codec.FrameCount);
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        if ((long)info.Width * info.Height > GifDecoder.MaxPixels)
        {
            throw new InvalidDataException("Animation too large");
        }

        // A frame that is built on something other than its predecessor needs that frame's pixels kept; keep only those.
        var keep = new HashSet<int>();
        for (var i = 0; i < infos.Length; i++)
        {
            if (infos[i].RequiredFrame >= 0 && infos[i].RequiredFrame != i - 1)
            {
                keep.Add(infos[i].RequiredFrame);
            }
        }
        var saved = new Dictionary<int, byte[]>();
        var buffer = new byte[info.BytesSize];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            for (var i = 0; i < count; i++)
            {
                var required = infos.Length > i ? infos[i].RequiredFrame : -1;
                if (required >= 0 && required != i - 1 && saved.TryGetValue(required, out var base_))
                {
                    base_.CopyTo(buffer, 0);
                }
                var options = new SKCodecOptions(i) { PriorFrame = required };
                var result = codec.GetPixels(info, handle.AddrOfPinnedObject(), info.RowBytes, options);
                if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
                {
                    throw new InvalidDataException($"Frame {i} could not be decoded ({result})");
                }
                if (keep.Contains(i))
                {
                    saved[i] = (byte[])buffer.Clone();
                }
                var ms = infos.Length > i ? infos[i].Duration : 0;
                yield return new AnimationFrame(info.Width, info.Height, buffer,
                    TimeSpan.FromMilliseconds(ms < MinDelayMs ? DefaultDelayMs : ms));
            }
        }
        finally
        {
            handle.Free();
        }
    }
}
