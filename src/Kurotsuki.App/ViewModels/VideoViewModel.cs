using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kurotsuki.App.Services;

namespace Kurotsuki.App.ViewModels;

/// <summary>Transport controls and state of the video shown in the viewer.</summary>
public sealed partial class VideoViewModel : ObservableObject, IDisposable
{
    private readonly VideoPlayback _playback;
    private readonly DispatcherTimer _timer;
    private bool _updating;

    public VideoViewModel(string path)
    {
        _playback = new VideoPlayback(path);
        _playback.FrameUpdated += OnFrame;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Tick());
        _timer.Start();
    }

    /// <summary>Raised for every new picture; the view redraws its image (the bitmap object is reused).</summary>
    public event Action? Redraw;

    [ObservableProperty] private Bitmap? _frame;
    [ObservableProperty] private bool _isPlaying = true;
    [ObservableProperty] private bool _muted;
    [ObservableProperty] private double _volume = 100;
    [ObservableProperty] private double _lengthMs = 1;
    [ObservableProperty] private double _seekValue;
    [ObservableProperty] private string _timeText = "0:00 / 0:00";

    private void OnFrame()
    {
        if (!ReferenceEquals(Frame, _playback.Frame))
        {
            Frame = _playback.Frame;
        }
        Redraw?.Invoke();
    }

    private void Tick()
    {
        _updating = true;
        try
        {
            var length = Math.Max(1, _playback.LengthMs);
            LengthMs = length;
            SeekValue = Math.Min(_playback.PositionMs, length);
            IsPlaying = _playback.IsPlaying;
            TimeText = $"{Format(_playback.PositionMs)} / {Format(_playback.LengthMs)}";
        }
        finally
        {
            _updating = false;
        }
    }

    private static string Format(long ms) => $"{ms / 60000}:{ms / 1000 % 60:00}";

    partial void OnSeekValueChanged(double value)
    {
        if (!_updating)
        {
            _playback.PositionMs = (long)value; // the user dragged the slider
        }
    }

    partial void OnMutedChanged(bool value) => _playback.Muted = value;

    partial void OnVolumeChanged(double value)
    {
        _playback.Volume = (int)Math.Round(value);
        if (value > 0 && Muted)
        {
            Muted = false; // raising the volume is a request to hear it
        }
    }

    /// <summary>Keyboard: arrows nudge the volume in steps of 5.</summary>
    public void ChangeVolume(double delta) => Volume = Math.Clamp(Volume + delta, 0, 100);

    public void SeekBy(long deltaMs) => _playback.Seek(deltaMs);

    [RelayCommand]
    private void TogglePause()
    {
        _playback.TogglePause();
        IsPlaying = !IsPlaying;
    }

    [RelayCommand] private void Back() => _playback.Seek(-10_000);
    [RelayCommand] private void Forward() => _playback.Seek(10_000);
    [RelayCommand] private void ToggleMute() => Muted = !Muted;

    public void Dispose()
    {
        _timer.Stop();
        _playback.FrameUpdated -= OnFrame;
        _playback.Dispose();
    }
}
