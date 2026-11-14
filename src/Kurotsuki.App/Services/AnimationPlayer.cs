using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Kurotsuki.Core.Logging;
using Kurotsuki.Core.Media;

namespace Kurotsuki.App.Services;

/// <summary>
/// Plays a sequence of frames (animated GIF, ugoira) in a loop by handing a new bitmap to <c>show</c> for each frame, so the
/// existing image pipeline (zoom, fit) keeps working. The source is enumerated lazily on a worker thread; memory stays at about
/// one frame however long the animation is.
/// </summary>
public sealed class AnimationPlayer : IDisposable
{
    private readonly CancellationTokenSource _cts = new();

    /// <param name="frames">Creates a fresh enumeration each loop (the GIF is decoded again from the start).</param>
    /// <param name="show">Called on the UI thread with each frame.</param>
    /// <param name="onError">Called on the UI thread when playing fails; playback stops.</param>
    public AnimationPlayer(Func<IEnumerable<AnimationFrame>> frames, Action<Bitmap> show, Action<string> onError)
    {
        _ = Task.Run(() => RunAsync(frames, show, onError, _cts.Token));
    }

    private static async Task RunAsync(Func<IEnumerable<AnimationFrame>> frames, Action<Bitmap> show, Action<string> onError, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var count = 0;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                foreach (var frame in frames())
                {
                    ct.ThrowIfCancellationRequested();
                    var started = clock.Elapsed;
                    var bitmap = ToBitmap(frame);
                    count++;
                    await Dispatcher.UIThread.InvokeAsync(() => show(bitmap));
                    // Waiting is the frame's delay minus what decoding and drawing took, so playback keeps its speed.
                    var wait = frame.Delay - (clock.Elapsed - started);
                    if (wait > TimeSpan.Zero)
                    {
                        await Task.Delay(wait, ct);
                    }
                }
                if (count <= 1)
                {
                    return; // a still image: nothing to loop
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            AppLog.Warning("Animation playback failed", e);
            Dispatcher.UIThread.Post(() => onError(e.Message));
        }
    }

    internal static WriteableBitmap ToBitmap(AnimationFrame frame)
    {
        var bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using var buffer = bitmap.Lock();
        var rowBytes = frame.Width * 4;
        for (var y = 0; y < frame.Height; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(frame.Rgba, y * rowBytes, buffer.Address + y * buffer.RowBytes, rowBytes);
        }
        return bitmap;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
