using CommunityToolkit.Mvvm.ComponentModel;
using Kurotsuki.Core.Booru;
using Kurotsuki.Core.I18n;

namespace Kurotsuki.Core.Download;

public enum DownloadStatus { Queued, Downloading, Done, Failed, Skipped }

/// <summary>A single post, or a whole pool saved as one comic archive.</summary>
public sealed partial class DownloadItem : ObservableObject
{
    public DownloadItem(Post post) => Post = post;

    public DownloadItem(Pool pool) => Pool = pool;

    /// <summary>The post for a single-file download, or null when this item is a pool.</summary>
    public Post? Post { get; }
    public Pool? Pool { get; }

    /// <summary>Cached JSON for the persistent queue; the item never changes while queued.</summary>
    internal string? Payload { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    [NotifyPropertyChangedFor(nameof(IsDone))]
    private DownloadStatus _status = DownloadStatus.Queued;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private double _progress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private string _message = "";

    /// <summary>Where the file (or archive) was written, once done.</summary>
    [ObservableProperty]
    private string? _savedPath;

    public string Key => Pool?.Key ?? Post!.Key;
    public string Kind => Pool is not null ? "pool" : "post";

    public string Title => Pool is not null
        ? Messages.Get("download.pool", Pool.DisplayName, Pool.PostCount)
        : $"{Post!.Id} - {Post.Image}";

    public bool IsActive => Status == DownloadStatus.Downloading;
    public bool IsFailed => Status == DownloadStatus.Failed;
    public bool IsDone => Status == DownloadStatus.Done;

    public string Label => Status switch
    {
        DownloadStatus.Queued => Messages.Get("download.queued"),
        DownloadStatus.Downloading => Messages.Get("download.downloading", Math.Round(Progress * 100)),
        DownloadStatus.Done => Messages.Get("download.done"),
        DownloadStatus.Skipped => Messages.Get("download.skipped"),
        DownloadStatus.Failed => Messages.Get("download.failed", Message),
        _ => "",
    };
}
