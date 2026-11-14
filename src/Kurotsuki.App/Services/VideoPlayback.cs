using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Kurotsuki.Core.Logging;
using LibVLCSharp.Shared;

namespace Kurotsuki.App.Services;

/// <summary>
/// Plays a video file with libvlc. Avalonia has no video control, so VLC renders into a memory buffer (BGRA, via the video
/// callbacks) and a UI timer copies each new picture into a <see cref="WriteableBitmap"/>. Audio goes straight to VLC's own output.
/// Throws <see cref="VideoUnavailableException"/> when libvlc is not installed, so the caller can fall back to "Open externally".
/// </summary>
public sealed class VideoPlayback : IDisposable
{
    public sealed class VideoUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

    private static readonly object InitGate = new();
    private static LibVLC? _lib;

    private readonly MediaPlayer _player;
    private readonly Media _media;
    private readonly object _bufferGate = new();
    private readonly DispatcherTimer _timer;
    private IntPtr _buffer;
    private int _width, _height, _pitch;
    private volatile bool _dirty;
    private bool _disposed;

    // The delegates must stay referenced for as long as VLC may call them.
    private readonly MediaPlayer.LibVLCVideoFormatCb _format;
    private readonly MediaPlayer.LibVLCVideoCleanupCb _cleanup;
    private readonly MediaPlayer.LibVLCVideoLockCb _lock;
    private readonly MediaPlayer.LibVLCVideoUnlockCb _unlock;
    private readonly MediaPlayer.LibVLCVideoDisplayCb _display;

    /// <summary>The picture shown now (same instance while the size does not change; <see cref="FrameUpdated"/> says when to redraw).</summary>
    public WriteableBitmap? Frame { get; private set; }
    public event Action? FrameUpdated;
    public int FramesShown { get; private set; }

    public VideoPlayback(string path)
    {
        var lib = Library();
        _player = new MediaPlayer(lib);
        _media = new Media(lib, path, FromType.FromPath);
        _format = Format;
        _cleanup = Cleanup;
        _lock = Lock;
        _unlock = Unlock;
        _display = Display;
        _player.SetVideoFormatCallbacks(_format, _cleanup);
        _player.SetVideoCallbacks(_lock, _unlock, _display);
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Pump());
        _timer.Start();
        _player.Play(_media);
    }

    private static LibVLC Library()
    {
        lock (InitGate)
        {
            if (_lib is not null)
            {
                return _lib;
            }
            try
            {
                LibVLCSharp.Shared.Core.Initialize();
                var options = new List<string> { "--no-xlib", "--quiet", "--no-video-title-show", "--no-snapshot-preview" };
                if (Environment.GetEnvironmentVariable("KT_VIDEO_NO_AUDIO") == "1")
                {
                    options.Add("--no-audio"); // tests
                }
                _lib = new LibVLC(options.ToArray());
                return _lib;
            }
            catch (Exception e) when (e is VLCException or DllNotFoundException or BadImageFormatException or InvalidOperationException)
            {
                AppLog.Warning("libvlc is not available", e);
                throw new VideoUnavailableException("libvlc was not found", e);
            }
        }
    }

    // ---- libvlc callbacks (run on VLC threads) ----------------------------------------------------------------

    private uint Format(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
    {
        Marshal.Copy("RV32"u8.ToArray(), 0, chroma, 4); // BGRA on little-endian machines
        lock (_bufferGate)
        {
            _width = (int)width;
            _height = (int)height;
            _pitch = _width * 4;
            pitches = (uint)_pitch;
            lines = (uint)_height;
            if (_buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_buffer);
            }
            _buffer = Marshal.AllocHGlobal(_pitch * _height);
        }
        return 1;
    }

    private void Cleanup(ref IntPtr opaque)
    {
    }

    private IntPtr Lock(IntPtr opaque, IntPtr planes)
    {
        Monitor.Enter(_bufferGate);
        Marshal.WriteIntPtr(planes, _buffer);
        return IntPtr.Zero;
    }

    private void Unlock(IntPtr opaque, IntPtr picture, IntPtr planes) => Monitor.Exit(_bufferGate);

    private void Display(IntPtr opaque, IntPtr picture) => _dirty = true;

    // ---- UI thread --------------------------------------------------------------------------------------------

    private void Pump()
    {
        if (!_dirty || _disposed)
        {
            return;
        }
        _dirty = false;
        // libvlc hands out a buffer padded to its coded size; the picture itself is the size it reports for the video.
        uint visibleW = 0, visibleH = 0;
        _player.Size(0, ref visibleW, ref visibleH);
        lock (_bufferGate)
        {
            if (_buffer == IntPtr.Zero || _width == 0)
            {
                return;
            }
            var w = visibleW > 0 ? Math.Min((int)visibleW, _width) : _width;
            var h = visibleH > 0 ? Math.Min((int)visibleH, _height) : _height;
            if (Frame is null || Frame.PixelSize.Width != w || Frame.PixelSize.Height != h)
            {
                Frame = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            }
            using var target = Frame.Lock();
            var row = new byte[w * 4];
            for (var y = 0; y < h; y++)
            {
                Marshal.Copy(_buffer + y * _pitch, row, 0, row.Length);
                Marshal.Copy(row, 0, target.Address + y * target.RowBytes, row.Length);
            }
        }
        FramesShown++;
        FrameUpdated?.Invoke();
    }

    // ---- controls ---------------------------------------------------------------------------------------------

    public bool IsPlaying => _player.IsPlaying;
    public long LengthMs => _player.Length;

    public long PositionMs
    {
        get => _player.Time;
        set => _player.Time = Math.Max(0, value);
    }

    public bool Muted
    {
        get => _player.Mute;
        set => _player.Mute = value;
    }

    /// <summary>0 to 100.</summary>
    public int Volume
    {
        get => _player.Volume;
        set => _player.Volume = Math.Clamp(value, 0, 100);
    }

    public void Play() => _player.SetPause(false);
    public void Pause() => _player.SetPause(true);
    public void TogglePause() => _player.Pause();
    public void Seek(long deltaMs) => PositionMs = Math.Clamp(PositionMs + deltaMs, 0, Math.Max(0, LengthMs - 500));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _timer.Stop();
        // Stopping can block while VLC finishes its last callback, so it is done off the UI thread.
        var player = _player;
        var media = _media;
        var buffer = () =>
        {
            lock (_bufferGate)
            {
                if (_buffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_buffer);
                    _buffer = IntPtr.Zero;
                }
            }
        };
        Task.Run(() =>
        {
            try
            {
                player.Stop();
                player.Dispose();
                media.Dispose();
            }
            catch (Exception e)
            {
                AppLog.Warning("Could not stop the video player cleanly", e);
            }
            buffer();
        });
    }
}
