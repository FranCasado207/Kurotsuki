using System.Runtime.InteropServices;
using Kurotsuki.Core.Media;
using SkiaSharp;

namespace Kurotsuki.App.Services;

/// <summary>Decodes JPEG/PNG/WebP/GIF stills with Skia (already in the app through Avalonia) into straight RGBA.</summary>
public sealed class SkiaFrameDecoder : IFrameDecoder
{
    public DecodedImage? Decode(byte[] data)
    {
        using var image = SKImage.FromEncodedData(data);
        if (image is null)
        {
            return null;
        }
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var rgba = new byte[info.BytesSize];
        var handle = GCHandle.Alloc(rgba, GCHandleType.Pinned);
        try
        {
            return image.ReadPixels(info, handle.AddrOfPinnedObject(), info.RowBytes, 0, 0)
                ? new DecodedImage(info.Width, info.Height, rgba) : null;
        }
        finally
        {
            handle.Free();
        }
    }
}
