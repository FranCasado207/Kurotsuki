using Kurotsuki.App.Services;
using Kurotsuki.Core.Media;

namespace Kurotsuki.App.Tests;

public class AnimationFormatTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Theory]
    [InlineData("anim.png")]
    [InlineData("anim.webp")]
    public void AnimatedPngAndWebpAreDetectedAndPlayedFrameByFrame(string file)
    {
        var path = Fixture(file);
        var apng = file.EndsWith(".png");
        Assert.True(apng ? ApngDecoder.IsAnimated(path) : SkiaAnimation.IsAnimated(path));

        var alphaAt = new List<byte>();
        var delays = new List<int>();
        foreach (var frame in apng ? ApngDecoder.Frames(path, new SkiaFrameDecoder()) : SkiaAnimation.Frames(path))
        {
            Assert.Equal((48, 32), (frame.Width, frame.Height));
            alphaAt.Add(frame.Rgba[(12 * frame.Width + 11) * 4 + 3]); // pixel (11, 12): inside the square of frame 0 only
            delays.Add((int)frame.Delay.TotalMilliseconds);
        }

        Assert.Equal(4, alphaAt.Count);
        Assert.Equal(255, alphaAt[0]);
        Assert.Equal(0, alphaAt[3]);
        Assert.Equal([80, 120, 80, 120], delays);
    }

    [Fact]
    public void AStillImageIsNotAnimated()
    {
        var path = Path.Combine(Path.GetTempPath(), "still-" + Guid.NewGuid().ToString("N") + ".png");
        using (var bitmap = new SkiaSharp.SKBitmap(8, 8))
        using (var image = SkiaSharp.SKImage.FromBitmap(bitmap))
        using (var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100))
        {
            File.WriteAllBytes(path, data.ToArray());
        }
        try
        {
            Assert.False(SkiaAnimation.IsAnimated(path));
            Assert.False(ApngDecoder.IsAnimated(path));
            Assert.False(SkiaAnimation.IsAnimated(Path.Combine(Path.GetTempPath(), "does-not-exist.webp")));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ApngSubRectanglesAndDisposeModesAreComposedOnTheCanvas()
    {
        // Frame 1 draws a red square and asks for the canvas to be restored afterwards; frame 2 draws a green one elsewhere.
        var frames = ApngDecoder.Frames(Fixture("dispose.png"), new SkiaFrameDecoder()).ToList();
        Assert.Equal(3, frames.Count);
        static (byte R, byte G, byte B) At(AnimationFrame f, int x, int y)
        {
            var i = (y * f.Width + x) * 4;
            return (f.Rgba[i], f.Rgba[i + 1], f.Rgba[i + 2]);
        }
        Assert.Equal((0, 0, 255), At(frames[0], 5, 5));
        Assert.Equal((255, 0, 0), At(frames[1], 5, 5));
        Assert.Equal((0, 0, 255), At(frames[2], 5, 5));   // restored to what was there before the red square
        Assert.Equal((0, 255, 0), At(frames[2], 25, 13));
        Assert.Equal((0, 0, 255), At(frames[1], 25, 13)); // and the green one is not there yet
        Assert.All(frames, f => Assert.Equal(100, (int)f.Delay.TotalMilliseconds));
    }
}
