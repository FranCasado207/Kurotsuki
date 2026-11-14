using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kurotsuki.App.Services;
using Kurotsuki.Core.I18n;

namespace Kurotsuki.App.ViewModels;

public sealed record TagCount(string Tag, int Count)
{
    public string Label => $"{Tag}  ·  {Count}";
}

/// <summary>One hidden post in the report, with a preview so the user can judge whether the blacklist got it right.</summary>
public sealed partial class HiddenItemViewModel : ObservableObject
{
    private readonly BlacklistReportViewModel _report;

    public HiddenItemViewModel(HiddenPost hidden, PostViewModel preview, BlacklistReportViewModel report)
    {
        Hidden = hidden;
        Preview = preview;
        _report = report;
    }

    public HiddenPost Hidden { get; }
    public PostViewModel Preview { get; }
    public Bitmap? Thumbnail => Preview.Thumbnail;
    public string Title => $"#{Hidden.Post.Id}  ·  {Hidden.Post.Width}×{Hidden.Post.Height}";
    public string BlockedBy => Messages.Get("next.blockedBy", Hidden.TagsText);
    public string OtherTags => string.Join(' ', Hidden.Post.TagList.Where(t => !Hidden.Tags.Contains(t)).Take(14));

    public void NotifyThumbnail() => OnPropertyChanged(nameof(Thumbnail));

    [RelayCommand]
    private void Show() => _report.Allow(this);

    [RelayCommand]
    private void Open() => _report.Open(this);
}

/// <summary>
/// What the blacklist hid from a grid. Each post can be brought back ("show anyway"): that is remembered for the post itself,
/// so it stays visible in later searches even though its tag is still blacklisted.
/// </summary>
public sealed partial class BlacklistReportViewModel : ObservableObject, IDisposable
{
    private readonly PostGridViewModel _grid;
    private readonly AppServices _services;
    private readonly CancellationTokenSource _cts = new();

    public BlacklistReportViewModel(PostGridViewModel grid, AppServices services)
    {
        _grid = grid;
        _services = services;
        foreach (var hidden in grid.Hidden)
        {
            var preview = new PostViewModel(hidden.Post, services);
            var item = new HiddenItemViewModel(hidden, preview, this);
            preview.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PostViewModel.Thumbnail))
                {
                    item.NotifyThumbnail();
                }
            };
            Items.Add(item);
            _ = preview.LoadThumbnailAsync(_cts.Token);
        }
        Refresh();
    }

    public ObservableCollection<HiddenItemViewModel> Items { get; } = [];
    public ObservableCollection<TagCount> Tags { get; } = [];

    [ObservableProperty] private string _summary = "";
    public bool IsEmpty => Items.Count == 0;

    public void Allow(HiddenItemViewModel item)
    {
        _grid.Restore(item.Hidden);
        Items.Remove(item);
        Refresh();
    }

    public void Open(HiddenItemViewModel item) =>
        _services.Navigator.OpenViewer(Items.Select(i => i.Hidden.Post).ToList(), Items.IndexOf(item));

    [RelayCommand]
    private void ShowAll()
    {
        foreach (var item in Items.ToList())
        {
            Allow(item);
        }
    }

    private void Refresh()
    {
        Tags.Clear();
        foreach (var group in Items.SelectMany(i => i.Hidden.Tags).GroupBy(t => t).OrderByDescending(g => g.Count()).ThenBy(g => g.Key))
        {
            Tags.Add(new TagCount(group.Key, group.Count()));
        }
        Summary = Items.Count == 0 ? Messages.Get("next.reportEmpty") : Messages.Get("next.reportIntro", Items.Count);
        OnPropertyChanged(nameof(IsEmpty));
    }

    public void Dispose() => _cts.Cancel();
}
