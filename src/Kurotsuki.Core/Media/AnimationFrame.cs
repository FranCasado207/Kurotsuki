namespace Kurotsuki.Core.Media;

/// <summary>One frame of an animation as a full canvas of straight (non-premultiplied) RGBA pixels.</summary>
public sealed record AnimationFrame(int Width, int Height, byte[] Rgba, TimeSpan Delay);

/// <summary>A decoded still image: what <see cref="IFrameDecoder"/> returns.</summary>
public sealed record DecodedImage(int Width, int Height, byte[] Rgba);

/// <summary>
/// Decodes JPEG/PNG/WebP/… frames. Core has no image library of its own; the app supplies one (Skia).
/// Returns null when the bytes are not an image it can read.
/// </summary>
public interface IFrameDecoder
{
    DecodedImage? Decode(byte[] data);
}
