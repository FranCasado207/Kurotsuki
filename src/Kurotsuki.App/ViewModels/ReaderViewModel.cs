using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kurotsuki.App.Services;
using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Config;
using Kurotsuki.Core.Download;
using Kurotsuki.Core.I18n;
using Kurotsuki.Core.Library;

namespace Kurotsuki.App.ViewModels;

public enum ReaderMode { Paged, Scroll }
public enum ReaderFit { Page, Width }

/// <summary>One page of the pool. The bitmap is loaded on demand and dropped when the memory budget needs room.</summary>
public sealed partial class ReaderPageViewModel(Post post, int number, ReaderViewModel reader) : ObservableObject
{
    private const double MaxDecodedSide = 6000;

    public Post Post { get; } = post;
    public int Number { get; } = number;

    [ObservableProperty] private Bitmap? _bitmap;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _error = "";

    /// <summary>Width / height from the API, or from the decoded image once known; 0 when unknown.</summary>
    public double Aspect => Bitmap is { } b ? b.Size.Width / b.Size.Height
        : Post.Width > 0 && Post.Height > 0 ? (double)Post.Width / Post.Height : 0;

    public bool IsWide => Aspect > 1.1;

    /// <summary>Scroll mode: the page is drawn this wide and as tall as its aspect ratio says (a guess of 3:4 until known).</summary>
    public double StripWidth => reader.StripWidth;
    public double StripHeight => reader.StripWidth / (Aspect > 0 ? Aspect : 0.75);

    public void NotifyLayout()
    {
        OnPropertyChanged(nameof(StripWidth));
        OnPropertyChanged(nameof(StripHeight));
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        if (Bitmap is not null || IsLoading)
        {
            return;
        }
        IsLoading = true;
        Error = "";
        try
        {
            var url = Post.FileUrl ?? Post.SampleUrl ?? throw new IOException(Messages.Get("viewer.noFile"));
            var path = await reader.Services.Cache.FetchAsync(url, ct);
            var bitmap = await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                var big = Math.Max(Post.Width, Post.Height) > MaxDecodedSide;
                // Decode very large pages smaller: a 10000 px page would cost 400 MB of memory.
                return big ? Bitmap.DecodeToWidth(stream, (int)(Post.Width >= Post.Height ? MaxDecodedSide : MaxDecodedSide * Post.Width / Post.Height))
                           : new Bitmap(stream);
            }, ct);
            Bitmap = bitmap;
            OnPropertyChanged(nameof(Aspect));
            NotifyLayout();
            reader.PageLoaded(this);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Error = Messages.Get("reader.loadError", e.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void Unload()
    {
        var old = Bitmap;
        Bitmap = null;
        old?.Dispose();
    }
}

public sealed partial class ReaderViewModel : ObservableObject, IDisposable
{
    private const double ZoomStep = 1.25;
    private const double MaxZoom = 5;
    private const int Ahead = 2;
    private static readonly HashSet<string> PageTypes = ["png", "jpg", "jpeg", "gif", "bmp", "webp"];

    internal readonly AppServices Services;
    private readonly PageBudget _budget;
    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<int> _visible = [];
    private List<int[]> _groups = [];
    private readonly int _startPage;

    public ReaderViewModel(AppServices services, Pool pool, BooruConfiguration booru, int startPage)
    {
        Services = services;
        Pool = pool;
        Booru = booru;
        _startPage = startPage;
        var c = services.Config;
        _mode = string.Equals(c.ReaderMode, "SCROLL", StringComparison.OrdinalIgnoreCase) ? ReaderMode.Scroll : ReaderMode.Paged;
        _fit = string.Equals(c.ReaderFit, "WIDTH", StringComparison.OrdinalIgnoreCase) ? ReaderFit.Width : ReaderFit.Page;
        _zoom = Math.Clamp(services.Prefs.ZoomOf(pool.Key) ?? 1, 1, MaxZoom);
        _rightToLeft = c.ReaderRightToLeft;
        _spread = c.ReaderSpread;
        _budget = new PageBudget((long)c.EffectiveReaderCacheMegabytes * 1024 * 1024);
        _title = pool.DisplayName;
        _ = LoadAsync();
    }

    public Pool Pool { get; }
    public BooruConfiguration Booru { get; }
    public ObservableCollection<ReaderPageViewModel> Pages { get; } = [];
    /// <summary>What the paged view shows now, left to right as drawn (already reversed for right-to-left).</summary>
    public ObservableCollection<ReaderPageViewModel> Shown { get; } = [];

    [ObservableProperty] private string _title;
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private string _hiddenText = "";
    [ObservableProperty] private int _currentGroup;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsPaged))] [NotifyPropertyChangedFor(nameof(IsScroll))] [NotifyPropertyChangedFor(nameof(ModeText))]
    private ReaderMode _mode;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FitText))] private ReaderFit _fit;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DirectionText))] private bool _rightToLeft;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SpreadText))] private bool _spread;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ZoomText))] private double _zoom = 1;
    [ObservableProperty] private double _stripWidth = 800;
    [ObservableProperty] private bool _isFinished;
    [ObservableProperty] private string _pageText = "";

    public bool IsPaged => Mode == ReaderMode.Paged;
    public bool IsScroll => Mode == ReaderMode.Scroll;
    public string ModeText => IsPaged ? Messages.Get("reader.modePaged") : Messages.Get("reader.modeScroll");
    public string FitText => Fit == ReaderFit.Page ? Messages.Get("reader.fitPage") : Messages.Get("reader.fitWidth");
    public string DirectionText => RightToLeft ? Messages.Get("reader.rtl") : Messages.Get("reader.ltr");
    public string SpreadText => Spread ? Messages.Get("reader.spreadOn") : Messages.Get("reader.spreadOff");
    public string ZoomText => $"{Math.Round(Zoom * 100)}%";
    public int GroupCount => _groups.Count;
    // ---- zoom, bookmarks and the next pool are remembered per pool (next-settings.json) ---------------------------------

    partial void OnZoomChanged(double value) => Services.Prefs.SetZoom(Pool.Key, value);

    /// <summary>The first page on screen; scroll mode keeps it in <see cref="CurrentGroup"/>.</summary>
    public int CurrentPage => _groups.Count == 0 ? 0 : IsScroll ? CurrentGroup : _groups[Math.Clamp(CurrentGroup, 0, _groups.Count - 1)][0];

    public IReadOnlyList<int> Bookmarks => Services.Prefs.BookmarksOf(Pool.Key);
    public bool HasBookmarks => Bookmarks.Count > 0;
    public bool IsBookmarked => Bookmarks.Contains(CurrentPage);

    [RelayCommand]
    private void ToggleBookmark()
    {
        if (Pages.Count == 0)
        {
            return;
        }
        Services.Prefs.ToggleBookmark(Pool.Key, CurrentPage);
        NotifyBookmarks();
    }

    private void NotifyBookmarks()
    {
        OnPropertyChanged(nameof(Bookmarks));
        OnPropertyChanged(nameof(HasBookmarks));
        OnPropertyChanged(nameof(IsBookmarked));
    }

    /// <summary>The pool that follows this one by name, once found.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasNextPool))] private Pool? _nextPool;
    public bool HasNextPool => NextPool is not null;

    private async Task FindNextPoolAsync()
    {
        try
        {
            if (PoolSeries.SearchTerm(Pool) is not { } term)
            {
                return;
            }
            var client = Services.Clients.ClientFor(Booru);
            var found = await client.SearchPoolsAsync(term, null, PoolOrder.Name, 0, 100, _cts.Token);
            NextPool = PoolSeries.Next(Pool, found);
        }
        catch (Exception)
        {
            // no "next pool" button is the fallback; the reader works without it
        }
    }

    public void OpenNextPool()
    {
        if (NextPool is { } next)
        {
            Services.Navigator.OpenReader(next, Booru, 0);
        }
    }

    public bool CanGoBack => CurrentGroup > 0;
    public bool CanGoForward => CurrentGroup < _groups.Count - 1;

    // ---- loading the pool -----------------------------------------------------------------------------------

    private async Task LoadAsync()
    {
        try
        {
            var client = Services.Clients.ClientFor(Booru);
            var all = await client.PoolPostsAsync(Pool, _cts.Token);
            var posts = all.Where(IsPage).ToList();
            var unsupported = all.Count - posts.Count;
            var hidden = 0;
            if (Services.Config.ReaderHonorBlacklist)
            {
                var blocked = new HashSet<string>(Services.Config.BlacklistedTags);
                var before = posts.Count;
                posts = posts.Where(p => !TagBlacklist.IsBlocked(p, blocked, Services.BlacklistAllowed)).ToList();
                hidden = before - posts.Count;
            }
            HiddenText = hidden > 0 ? Messages.Get("reader.hidden", hidden) : "";
            if (posts.Count == 0)
            {
                Error = unsupported > 0 ? Messages.Get("next.noReaderPages") : Messages.Get("pools.postsEmpty");
                return;
            }
            for (var i = 0; i < posts.Count; i++)
            {
                Pages.Add(new ReaderPageViewModel(posts[i], i, this));
            }
            RebuildGroups();
            var startGroup = SpreadLayout.GroupOf(_groups, Math.Clamp(_startPage, 0, Pages.Count - 1));
            GoToGroup(startGroup);
            _ = FindNextPoolAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Error = Messages.Get("pools.error", e.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static bool IsPage(Post post)
    {
        var dot = post.Image?.LastIndexOf('.') ?? -1;
        return post.FileUrl is not null && dot >= 0 && PageTypes.Contains(post.Image![(dot + 1)..].ToLowerInvariant());
    }

    // ---- groups / navigation --------------------------------------------------------------------------------

    private void RebuildGroups()
    {
        var current = _groups.Count > 0 ? _groups[Math.Clamp(CurrentGroup, 0, _groups.Count - 1)][0] : _startPage;
        _groups = Spread && IsPaged
            ? SpreadLayout.Spreads(Pages.Count, i => Pages[i].IsWide, coverAlone: true)
            : SpreadLayout.Singles(Pages.Count);
        OnPropertyChanged(nameof(GroupCount));
        CurrentGroup = SpreadLayout.GroupOf(_groups, Math.Clamp(current, 0, Math.Max(0, Pages.Count - 1)));
    }

    private void GoToGroup(int group)
    {
        if (_groups.Count == 0)
        {
            return;
        }
        CurrentGroup = Math.Clamp(group, 0, _groups.Count - 1);
        UpdateShown();
    }

    private void UpdateShown()
    {
        var pages = _groups[CurrentGroup].Select(i => Pages[i]).ToList();
        if (RightToLeft)
        {
            pages.Reverse(); // in a spread the first page is on the right
        }
        Shown.Clear();
        foreach (var page in pages)
        {
            Shown.Add(page);
        }
        var numbers = _groups[CurrentGroup];
        PageText = numbers.Length == 1
            ? Messages.Get("reader.page", numbers[0] + 1, Pages.Count)
            : Messages.Get("reader.pageSpread", numbers[0] + 1, numbers[^1] + 1, Pages.Count);
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(IsBookmarked));

        // Preload what is shown and the next pages in both directions; those are protected from eviction.
        _visible.Clear();
        for (var g = Math.Max(0, CurrentGroup - 1); g <= Math.Min(_groups.Count - 1, CurrentGroup + Ahead); g++)
        {
            foreach (var p in _groups[g])
            {
                _visible.Add(p);
                _ = Pages[p].LoadAsync(_cts.Token);
                _budget.Touch(p);
            }
        }
        Record(numbers);
    }

    private void Record(int[] shown)
    {
        // The last page shown counts: the pool is "finished" once its final page was on screen.
        Services.Reading.Record(Pool, shown[^1], Pages.Count);
        IsFinished = Services.Reading.IsFinished(Pool.Key);
    }

    /// <summary>Scroll mode reports which page it is looking at.</summary>
    public void ScrolledTo(int page)
    {
        if (!IsScroll || page < 0 || page >= Pages.Count)
        {
            return;
        }
        CurrentGroup = page;
        PageText = Messages.Get("reader.page", page + 1, Pages.Count);
        OnPropertyChanged(nameof(IsBookmarked));
        Services.Reading.Record(Pool, page, Pages.Count);
        IsFinished = Services.Reading.IsFinished(Pool.Key);
    }

    /// <summary>Scroll mode: pages the view currently has on screen (never evicted); load them.</summary>
    public void SetVisiblePages(IEnumerable<int> pages)
    {
        _visible.Clear();
        foreach (var p in pages)
        {
            _visible.Add(p);
            _ = Pages[p].LoadAsync(_cts.Token);
            _budget.Touch(p);
        }
    }

    internal void PageLoaded(ReaderPageViewModel page)
    {
        var size = page.Bitmap is { } b ? PageBudget.Estimate(b.PixelSize.Width, b.PixelSize.Height) : 0;
        _budget.Add(page.Number, size);
        foreach (var dropped in _budget.Evict(_visible))
        {
            Pages[dropped].Unload();
        }
        if (Spread && IsPaged)
        {
            // A page turned out wide: the pairing may change.
            var before = _groups.Count;
            RebuildGroups();
            if (_groups.Count != before)
            {
                UpdateShown();
            }
        }
    }

    [RelayCommand]
    private void Next() => Step(1);

    [RelayCommand]
    private void Previous() => Step(-1);

    /// <summary>Direction-independent: +1 is forward in reading order.</summary>
    public void Step(int direction)
    {
        if (IsPaged)
        {
            GoToGroup(CurrentGroup + direction);
        }
    }

    public void GoToPage(int page) => GoToGroup(SpreadLayout.GroupOf(_groups, page));

    // ---- view options ---------------------------------------------------------------------------------------

    [RelayCommand]
    private void ToggleMode()
    {
        Mode = IsPaged ? ReaderMode.Scroll : ReaderMode.Paged;
        Services.Config.ReaderMode = Mode == ReaderMode.Paged ? "PAGED" : "SCROLL";
        var page = _groups.Count > 0 ? _groups[Math.Clamp(CurrentGroup, 0, _groups.Count - 1)][0] : 0;
        RebuildGroups();
        if (IsPaged)
        {
            GoToGroup(SpreadLayout.GroupOf(_groups, page));
        }
        Services.SaveConfig();
    }

    [RelayCommand]
    private void ToggleFit()
    {
        Fit = Fit == ReaderFit.Page ? ReaderFit.Width : ReaderFit.Page;
        Services.Config.ReaderFit = Fit == ReaderFit.Page ? "PAGE" : "WIDTH";
        Services.SaveConfig();
    }

    [RelayCommand]
    private void ToggleDirection()
    {
        RightToLeft = !RightToLeft;
        Services.Config.ReaderRightToLeft = RightToLeft;
        if (_groups.Count > 0)
        {
            UpdateShown();
        }
        Services.SaveConfig();
    }

    [RelayCommand]
    private void ToggleSpread()
    {
        Spread = !Spread;
        Services.Config.ReaderSpread = Spread;
        if (Pages.Count > 0)
        {
            RebuildGroups();
            UpdateShown();
        }
        Services.SaveConfig();
    }

    public void ZoomBy(double factor) => Zoom = Math.Clamp(Zoom * factor, 1, MaxZoom);

    [RelayCommand] private void ZoomIn() => ZoomBy(ZoomStep);
    [RelayCommand] private void ZoomOut() => ZoomBy(1 / ZoomStep);
    [RelayCommand] private void ResetZoom() => Zoom = 1;

    partial void OnStripWidthChanged(double value)
    {
        foreach (var page in Pages)
        {
            page.NotifyLayout();
        }
    }

    [RelayCommand]
    private void ToggleFinished()
    {
        Services.Reading.SetFinished(Pool, !Services.Reading.IsFinished(Pool.Key));
        IsFinished = Services.Reading.IsFinished(Pool.Key);
    }

    [RelayCommand]
    private void Download() => Services.Queue.DownloadPool(Pool);

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        foreach (var page in Pages)
        {
            page.Unload();
        }
    }
}
