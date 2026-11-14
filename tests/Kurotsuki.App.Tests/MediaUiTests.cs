using System.Diagnostics;
using System.IO.Compression;
using Avalonia.Media.Imaging;
using Kurotsuki.App.Services;
using Kurotsuki.Core.Media;

namespace Kurotsuki.App.Tests;

public class MediaUiTests
{
    private static string? Tool(string name) => Environment.GetEnvironmentVariable("PATH")!.Split(':')
        .Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists);

    private static string Make(string extension, params string[] args)
    {
        var ffmpeg = Tool("ffmpeg")!;
        var file = Path.Combine(Harness.Root, "media-" + Guid.NewGuid().ToString("N") + extension);
        Directory.CreateDirectory(Harness.Root);
        var info = new ProcessStartInfo(ffmpeg) { RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in new[] { "-v", "error", "-y" }.Concat(args).Append(file))
        {
            info.ArgumentList.Add(a);
        }
        using var p = Process.Start(info)!;
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, p.StandardError.ReadToEnd());
        return file;
    }

    [Fact]
    public Task VideoPlaysThroughLibVlcAndProducesRealPicturesAndSeeks() => Harness.Run(async () =>
    {
        if (Tool("ffmpeg") is null)
        {
            return;
        }
        var video = Make(".mp4", "-f", "lavfi", "-i", "testsrc=size=160x120:rate=25:duration=4", "-pix_fmt", "yuv420p");
        VideoPlayback player;
        try
        {
            player = new VideoPlayback(video);
        }
        catch (VideoPlayback.VideoUnavailableException)
        {
            return; // libvlc is not installed on this machine
        }
        try
        {
            var updates = 0;
            player.FrameUpdated += () => updates++;
            await Harness.WaitFor(() => player.FramesShown >= 5, 10000);

            Assert.NotNull(player.Frame);
            Assert.Equal((160, 120), (player.Frame!.PixelSize.Width, player.Frame.PixelSize.Height));
            Assert.True(updates >= 5);
            // the test pattern is colourful: a black or flat picture would mean the callbacks delivered nothing
            var pixels = new byte[160 * 120 * 4];
            using (var fb = player.Frame.Lock())
            {
                for (var y = 0; y < 120; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(fb.Address + y * fb.RowBytes, pixels, y * 160 * 4, 160 * 4);
                }
            }
            Assert.True(pixels.Distinct().Count() > 30, "picture looks flat or empty");

            await Harness.WaitFor(() => player.LengthMs > 3000, 5000);
            Assert.InRange(player.LengthMs, 3500, 4500);
            player.Pause();
            await Task.Delay(300);
            var at = player.PositionMs;
            player.PositionMs = 2500;
            await Harness.WaitFor(() => Math.Abs(player.PositionMs - 2500) < 600, 5000);
            Assert.True(player.PositionMs > at);
        }
        finally
        {
            player.Dispose();
        }
    });

    [Fact]
    public Task AnimatedGifPlaysAndLoopsFrameByFrame() => Harness.Run(async () =>
    {
        if (Tool("ffmpeg") is null)
        {
            return;
        }
        var gif = Make(".gif", "-f", "lavfi", "-i", "testsrc=size=64x48:rate=20:duration=0.5");
        var shown = new List<Bitmap>();
        string? error = null;
        using var player = new AnimationPlayer(() => GifDecoder.Frames(gif), shown.Add, e => error = e);
        await Harness.WaitFor(() => shown.Count >= 25, 10000); // 10 frames per loop: it looped at least twice
        Assert.Null(error);
        Assert.All(shown, b => Assert.Equal((64, 48), (b.PixelSize.Width, b.PixelSize.Height)));
    });

    [Fact]
    public Task UgoiraZipPlaysThroughTheSkiaDecoder() => Harness.Run(async () =>
    {
        if (Tool("ffmpeg") is null)
        {
            return;
        }
        // real JPEG frames, as Danbooru's ugoira zips contain
        var dir = Path.Combine(Harness.Root, "ug-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Make(".jpg", "-f", "lavfi", "-i", "testsrc=size=96x64:rate=1:duration=1", "-frames:v", "1");
        var jpgs = Directory.GetFiles(Harness.Root, "media-*.jpg");
        var zip = Path.Combine(dir, "u.zip");
        using (var z = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
        {
            for (var i = 0; i < 3; i++)
            {
                z.CreateEntryFromFile(jpgs[0], $"{i}.jpg");
            }
            using var s = z.CreateEntry("animation.json").Open();
            s.Write(System.Text.Encoding.UTF8.GetBytes("{\"frames\":[{\"file\":\"0.jpg\",\"delay\":40},{\"file\":\"1.jpg\",\"delay\":40},{\"file\":\"2.jpg\",\"delay\":40}]}"));
        }
        var decoder = new SkiaFrameDecoder();
        var frames = Ugoira.Read(zip);
        var decoded = Ugoira.Frames(frames, decoder).ToList();
        Assert.Equal(3, decoded.Count);
        Assert.Equal((96, 64), (decoded[0].Width, decoded[0].Height));
        Assert.True(decoded[0].Rgba.Distinct().Count() > 30);

        var gif = Ugoira.ConvertToGif(zip, Ugoira.GifNameFor(zip), decoder);
        Assert.Equal(3, GifDecoder.Frames(gif).Count());

        var shown = new List<Bitmap>();
        using var player = new AnimationPlayer(() => Ugoira.Frames(frames, decoder), shown.Add, _ => { });
        await Harness.WaitFor(() => shown.Count >= 7, 10000);
        await Task.CompletedTask;
    });

    [Fact]
    public Task ViewerPlaysAGifAnUgoiraAndAVideoOneAfterTheOther() => Harness.Run(async () =>
    {
        if (Tool("ffmpeg") is null)
        {
            return;
        }
        var gif = Make(".gif", "-f", "lavfi", "-i", "testsrc=size=80x60:rate=20:duration=0.5");
        var video = Make(".mp4", "-f", "lavfi", "-i", "testsrc=size=160x120:rate=25:duration=3", "-pix_fmt", "yuv420p");
        var jpg = Make(".jpg", "-f", "lavfi", "-i", "testsrc=size=96x64:rate=1:duration=1", "-frames:v", "1");
        var zip = Path.Combine(Harness.Root, "ugoira.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            for (var i = 0; i < 3; i++) z.CreateEntryFromFile(jpg, $"{i}.jpg");
            using var s = z.CreateEntry("animation.json").Open();
            s.Write(System.Text.Encoding.UTF8.GetBytes("{\"frames\":[{\"file\":\"0.jpg\",\"delay\":50},{\"file\":\"1.jpg\",\"delay\":50},{\"file\":\"2.jpg\",\"delay\":50}]}"));
        }
        StubBooru.Files["/media/anim.gif"] = File.ReadAllBytes(gif);
        StubBooru.Files["/media/ug.zip"] = File.ReadAllBytes(zip);
        StubBooru.Files["/media/clip.mp4"] = File.ReadAllBytes(video);

        var services = Harness.NewServices();
        var main = new Views.MainWindow { DataContext = new ViewModels.MainViewModel(services), Width = 1200, Height = 800 };
        main.Show();
        Kurotsuki.Core.Booru.Post P(int id, string image) => new()
        {
            Id = id, Image = image, FileUrl = $"{StubBooru.Host}/media/{image}", PreviewUrl = $"{StubBooru.Host}/media/t.png",
            BooruBaseUrl = StubBooru.Host, Tags = "t", Width = 80, Height = 60,
        };
        ((ViewModels.MainViewModel)main.DataContext!).OpenViewer([P(1, "anim.gif"), P(2, "ug.zip"), P(3, "clip.mp4")], 0);
        var viewer = ((ViewModels.MainViewModel)main.DataContext!).Viewer!;
        var vm = (ViewModels.ViewerViewModel)viewer.DataContext!;

        // GIF: successive, different bitmaps arrive (it animates and loops)
        var seen = new HashSet<Bitmap>();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Image) && vm.Image is { } b) seen.Add(b); };
        await Harness.WaitFor(() => seen.Count >= 12, 10000);
        Assert.True(vm.IsAnimated);
        Assert.Equal((80, 60), (vm.Image!.PixelSize.Width, vm.Image.PixelSize.Height));
        Harness.Capture(viewer, "viewer-gif");

        // ugoira: frames come from the zip through the Skia decoder
        vm.NextCommand.Execute(null);
        seen.Clear();
        await Harness.WaitFor(() => seen.Count >= 6 && vm.Post.Id == 2, 10000);
        Assert.True(vm.IsAnimated);
        Assert.Equal((96, 64), (vm.Image!.PixelSize.Width, vm.Image.PixelSize.Height));
        Assert.Equal("", vm.Error);

        // video: the controls appear and pictures arrive
        vm.NextCommand.Execute(null);
        await Harness.WaitFor(() => vm.HasVideo && vm.Video!.Frame is not null, 15000);
        Assert.False(vm.IsAnimated);
        Assert.Equal("", vm.Error);
        await Harness.WaitFor(() => vm.Video!.TimeText != "0:00 / 0:00", 5000);
        Harness.Capture(viewer, "viewer-video");
        vm.Video!.TogglePauseCommand.Execute(null);
        Assert.False(vm.Video.IsPlaying);

        // volume and keyboard seek: the slider drives libvlc, and raising it un-mutes
        vm.Video.ToggleMuteCommand.Execute(null);
        Assert.True(vm.Video.Muted);
        vm.Video.ChangeVolume(-30);
        Assert.Equal(70, vm.Video.Volume);
        Assert.False(vm.Video.Muted);
        vm.Video.ChangeVolume(-500);
        Assert.Equal(0, vm.Video.Volume);
        vm.Video.SeekBy(1000);

        // leaving the video stops it and removes the controls
        vm.PreviousCommand.Execute(null);
        await Harness.WaitFor(() => !vm.HasVideo);
        viewer.Close();
        main.Close();
    });
}
