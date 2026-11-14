using System.IO.Compression;
using System.Text;
using Kurotsuki.Core.Media;

namespace Kurotsuki.Core.Tests;

public class MediaTests
{
    /// <summary>A "decoder" for test frames stored as: width (2), height (2), then RGBA bytes.</summary>
    private sealed class RawDecoder : IFrameDecoder
    {
        public DecodedImage? Decode(byte[] data)
        {
            if (data.Length < 4)
            {
                return null;
            }
            int w = data[0] | data[1] << 8, h = data[2] | data[3] << 8;
            return data.Length == 4 + w * h * 4 ? new DecodedImage(w, h, data[4..]) : null;
        }
    }

    private static byte[] Raw(int w, int h, Func<int, int, (byte R, byte G, byte B, byte A)> pixel)
    {
        var data = new byte[4 + w * h * 4];
        data[0] = (byte)w; data[1] = (byte)(w >> 8); data[2] = (byte)h; data[3] = (byte)(h >> 8);
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var (r, g, b, a) = pixel(x, y);
                var at = 4 + (y * w + x) * 4;
                data[at] = r; data[at + 1] = g; data[at + 2] = b; data[at + 3] = a;
            }
        return data;
    }

    private static AnimationFrame Frame(int w, int h, int shift, int delayMs, bool transparentCorner = false)
    {
        var raw = Raw(w, h, (x, y) => ((byte)(x * 255 / w), (byte)((y + shift) * 255 / h % 256), (byte)(128 + shift * 4),
            transparentCorner && x < w / 4 && y < h / 4 ? (byte)0 : (byte)255));
        return new AnimationFrame(w, h, raw[4..], TimeSpan.FromMilliseconds(delayMs));
    }

    private static string TempPath(string ext) => Path.Combine(Path.GetTempPath(), "kt-media-" + Guid.NewGuid().ToString("N") + ext);

    [Fact]
    public void GifRoundTripKeepsSizeDelaysCountAndColoursClose()
    {
        var frames = Enumerable.Range(0, 5).Select(i => Frame(64, 48, i * 6, 40 + i * 20)).ToList();
        var file = TempPath(".gif");
        using (var o = File.Create(file))
        {
            GifEncoder.Write(o, frames);
        }

        var decoded = GifDecoder.Frames(file).ToList();
        Assert.Equal(5, decoded.Count);
        Assert.Equal((64, 48), GifDecoder.ReadSize(file));
        Assert.Equal([40.0, 60, 80, 100, 120], decoded.Select(f => f.Delay.TotalMilliseconds));
        for (var f = 0; f < frames.Count; f++)
        {
            // median cut with 15-bit colours: every channel should be within a few levels
            var worst = 0;
            for (var i = 0; i < frames[f].Rgba.Length; i += 4)
            {
                for (var c = 0; c < 3; c++)
                {
                    worst = Math.Max(worst, Math.Abs(frames[f].Rgba[i + c] - decoded[f].Rgba[i + c]));
                }
            }
            Assert.True(worst <= 24, $"frame {f}: worst channel error {worst}");
        }
    }

    [Fact]
    public void GifKeepsTransparency()
    {
        var file = TempPath(".gif");
        using (var o = File.Create(file))
        {
            GifEncoder.Write(o, [Frame(40, 40, 0, 100, transparentCorner: true)]);
        }
        var frame = GifDecoder.Frames(file).Single();
        Assert.Equal(0, frame.Rgba[3]); // top-left pixel transparent
        Assert.Equal(255, frame.Rgba[(39 * 40 + 39) * 4 + 3]);
    }

    [Fact]
    public void GifOutputIsAcceptedByFfprobeWhenAvailable()
    {
        var file = TempPath(".gif");
        using (var o = File.Create(file))
        {
            GifEncoder.Write(o, Enumerable.Range(0, 6).Select(i => Frame(90, 60, i * 5, 80)));
        }
        var ffprobe = ExternalTools.Find("ffprobe");
        if (ffprobe is null)
        {
            return;
        }
        var result = ExternalTools.Run(ffprobe, "-v", "error", "-count_frames", "-select_streams", "v:0",
            "-show_entries", "stream=width,height,nb_read_frames,codec_name", "-of", "default=nw=1", file);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("codec_name=gif", result.Output);
        Assert.Contains("width=90", result.Output);
        Assert.Contains("height=60", result.Output);
        Assert.Contains("nb_read_frames=6", result.Output);
    }

    [Fact]
    public void DecoderReadsGifsMadeByFfmpegIncludingDisposalAndInterlace()
    {
        var ffmpeg = ExternalTools.Find("ffmpeg");
        if (ffmpeg is null)
        {
            return;
        }
        var file = TempPath(".gif");
        // 10 frames of a moving test pattern at 10 fps; ffmpeg's encoder uses transparency/disposal optimisations
        var run = ExternalTools.Run(ffmpeg, "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc=size=120x80:rate=10:duration=1", file);
        Assert.True(run.ExitCode == 0, run.Output);

        var frames = GifDecoder.Frames(file).ToList();
        Assert.Equal(10, frames.Count);
        Assert.All(frames, f => Assert.Equal(120 * 80 * 4, f.Rgba.Length));
        Assert.All(frames, f => Assert.InRange(f.Delay.TotalMilliseconds, 90, 110));
        // frames must differ (the pattern moves) and not be blank
        Assert.NotEqual(frames[0].Rgba, frames[9].Rgba);
        Assert.Contains(frames[5].Rgba, b => b != 0);
    }

    [Fact]
    public void LzwRoundTripsRandomAndRepetitiveData()
    {
        var random = new Random(1);
        foreach (var data in new[]
                 {
                     Enumerable.Range(0, 20000).Select(_ => (byte)random.Next(256)).ToArray(),
                     Enumerable.Repeat((byte)7, 50000).ToArray(),
                     Enumerable.Range(0, 30000).Select(i => (byte)(i / 50 % 4)).ToArray(),
                 })
        {
            var encoded = GifEncoder.LzwEncode(8, data);
            Assert.Equal(data, GifDecoder.LzwDecode(8, encoded, data.Length));
        }
    }

    [Fact]
    public void DecoderRejectsNonGifsAndHugeCanvases()
    {
        Assert.Throws<InvalidDataException>(() => GifDecoder.Frames(new MemoryStream(Encoding.ASCII.GetBytes("PNG not a gif........"))).ToList());
        var huge = new byte[13];
        "GIF89a"u8.CopyTo(huge);
        huge[6] = huge[8] = 0xFF; huge[7] = huge[9] = 0xFF; // 65535 x 65535
        Assert.Throws<InvalidDataException>(() => GifDecoder.Frames(new MemoryStream(huge)).ToList());
    }

    // ---- ugoira -------------------------------------------------------------------------------------------

    private static string MakeUgoira(bool withMetadata, int frames = 4, int w = 32, int h = 24, bool differentSize = false)
    {
        var path = TempPath(".zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        // names are in reverse alphabetical order inside the metadata to prove the order comes from animation.json
        var names = Enumerable.Range(0, frames).Select(i => $"{i:000000}.png").ToList();
        for (var i = 0; i < frames; i++)
        {
            using var s = zip.CreateEntry(names[i]).Open();
            var ww = differentSize && i == 2 ? w + 4 : w;
            s.Write(Raw(ww, h, (x, y) => ((byte)(x * 8), (byte)(y * 10), (byte)(i * 50), 255)));
        }
        if (withMetadata)
        {
            var order = Enumerable.Range(0, frames).Reverse().Select(i => $"{{\"file\":\"{names[i]}\",\"delay\":{50 + i * 10}}}");
            using var s = zip.CreateEntry("animation.json").Open();
            s.Write(Encoding.UTF8.GetBytes($"{{\"frames\":[{string.Join(",", order)}]}}"));
        }
        return path;
    }

    [Fact]
    public void UgoiraFramesFollowAnimationJsonOrderAndDelays()
    {
        var frames = Ugoira.Read(MakeUgoira(withMetadata: true));
        Assert.Equal(["000003.png", "000002.png", "000001.png", "000000.png"], frames.Select(f => f.Name));
        Assert.Equal([80, 70, 60, 50], frames.Select(f => f.DelayMs));
        Assert.DoesNotContain(frames, f => f.Name == "animation.json");
    }

    [Fact]
    public void UgoiraWithoutMetadataUsesSortedNamesAndDefaultDelay()
    {
        var frames = Ugoira.Read(MakeUgoira(withMetadata: false));
        Assert.Equal(["000000.png", "000001.png", "000002.png", "000003.png"], frames.Select(f => f.Name));
        Assert.All(frames, f => Assert.Equal(Ugoira.DefaultDelayMs, f.DelayMs));
    }

    [Fact]
    public void UgoiraConvertsToAGifThatReadsBack()
    {
        var zip = MakeUgoira(withMetadata: true);
        var gif = Ugoira.ConvertToGif(zip, Ugoira.GifNameFor(zip), new RawDecoder());
        Assert.EndsWith(".gif", gif);
        Assert.False(File.Exists(gif + ".part"));
        var decoded = GifDecoder.Frames(gif).ToList();
        Assert.Equal(4, decoded.Count);
        Assert.Equal([80.0, 70, 60, 50], decoded.Select(f => f.Delay.TotalMilliseconds));
        Assert.Equal((32, 24), (decoded[0].Width, decoded[0].Height));
    }

    [Fact]
    public void UgoiraConversionRefusesMixedFrameSizesAndUnreadableFrames()
    {
        var mixed = MakeUgoira(withMetadata: false, differentSize: true);
        Assert.Throws<IOException>(() => Ugoira.ConvertToGif(mixed, mixed + ".gif", new RawDecoder()));
        Assert.False(File.Exists(mixed + ".gif")); // nothing half written is left behind

        var bad = TempPath(".zip");
        using (var z = ZipFile.Open(bad, ZipArchiveMode.Create))
        {
            using var s = z.CreateEntry("a.png").Open();
            s.Write([1, 2, 3]);
        }
        Assert.Throws<IOException>(() => Ugoira.ConvertToGif(bad, bad + ".gif", new RawDecoder()));
    }

    [Fact]
    public void UgoiraRejectsArchivesWithoutImagesOrTooManyFrames()
    {
        var empty = TempPath(".zip");
        using (var z = ZipFile.Open(empty, ZipArchiveMode.Create))
        {
            using var s = z.CreateEntry("readme.txt").Open();
            s.Write([1]);
        }
        Assert.Throws<IOException>(() => Ugoira.Read(empty));
        Assert.Throws<IOException>(() => Ugoira.Read(MakeUgoira(false, frames: Ugoira.MaxFrames + 1, w: 1, h: 1)));
        Assert.True(Ugoira.IsUgoiraZip("x.ZIP"));
        Assert.False(Ugoira.IsUgoiraZip("x.cbz"));
    }
}
