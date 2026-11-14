using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Kurotsuki.Core.Download;
using Kurotsuki.Core.I18n;

namespace Kurotsuki.App.Services;

/// <summary>
/// System-tray icon (StatusNotifier on KDE/GNOME, notification area on Windows, menu bar on macOS) that shows the overall
/// download progress as a ring while anything is downloading, flashes a check when the batch finishes, then goes away.
/// </summary>
public sealed class DownloadTray : IDisposable
{
    private static readonly TimeSpan DoneLinger = TimeSpan.FromSeconds(4);
    private readonly AppServices _services;
    private readonly TrayIcon _tray = new();
    private readonly NativeMenuItem _summary = new() { IsEnabled = false };
    private readonly DispatcherTimer _hideTimer = new() { Interval = DoneLinger };
    private TrayState _state = TrayState.Idle;
    private int _percent = -1;

    public DownloadTray(AppServices services)
    {
        _services = services;
        var show = new NativeMenuItem(Messages.Get("next.trayShow"));
        show.Click += (_, _) => ShowWindow();
        _tray.Menu = [_summary, new NativeMenuItemSeparator(), show];
        _tray.Clicked += (_, _) => ShowWindow();
        _tray.IsVisible = false;
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (_state == TrayState.Done)
            {
                Apply(TrayState.Idle, 0);
            }
        };
        services.Queue.Items.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
            {
                foreach (DownloadItem item in e.NewItems)
                {
                    item.PropertyChanged += (_, args) =>
                    {
                        if (args.PropertyName is nameof(DownloadItem.Status) or nameof(DownloadItem.Progress))
                        {
                            Refresh();
                        }
                    };
                }
            }
            Refresh();
        };
    }

    public TrayIcon Icon => _tray;

    public readonly record struct Snapshot(bool Active, int Started, int Finished, double Progress)
    {
        public int Percent => (int)Math.Round(Progress * 100);
    }

    /// <summary>
    /// Progress of what was started. Items still waiting to be started are not part of it, so adding posts to the
    /// queue without pressing Start does not drag the ring backwards.
    /// </summary>
    public static Snapshot Measure(IEnumerable<DownloadItem> items)
    {
        var started = items.Where(i => i.Status != DownloadStatus.Queued).ToList();
        if (started.Count == 0)
        {
            return new Snapshot(false, 0, 0, 0);
        }
        static bool Over(DownloadItem i) => i.Status is DownloadStatus.Done or DownloadStatus.Skipped or DownloadStatus.Failed;
        var progress = started.Sum(i => Over(i) ? 1 : i.Progress) / started.Count;
        return new Snapshot(started.Any(i => i.Status == DownloadStatus.Downloading), started.Count,
            started.Count(Over), Math.Clamp(progress, 0, 1));
    }

    private void Refresh()
    {
        var snapshot = Measure(_services.Queue.Items);
        if (snapshot.Active)
        {
            _hideTimer.Stop();
            Apply(TrayState.Downloading, snapshot.Progress, snapshot);
        }
        else if (_state == TrayState.Downloading)
        {
            Apply(TrayState.Done, 1, snapshot);
            _hideTimer.Start();
        }
    }

    private void Apply(TrayState state, double progress, Snapshot snapshot = default)
    {
        var percent = (int)Math.Round(progress * 100);
        if (state == _state && percent == _percent)
        {
            return;
        }
        _state = state;
        _percent = percent;
        if (state == TrayState.Idle)
        {
            _tray.IsVisible = false;
            return;
        }
        _tray.Icon = new WindowIcon(TrayIconRenderer.Render(progress, state, TrayIconRenderer.ThemePalette()));
        var text = Messages.Get(state == TrayState.Done ? "next.trayDone" : "next.trayTooltip", snapshot.Finished, snapshot.Started, percent);
        _tray.ToolTipText = text;
        _summary.Header = text;
        _tray.IsVisible = true;
    }

    private static void ShowWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }
            window.Show();
            window.Activate();
        }
    }

    public void Dispose()
    {
        _hideTimer.Stop();
        _tray.IsVisible = false;
        _tray.Dispose();
    }
}
