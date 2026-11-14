using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kurotsuki.App.Services;
using Kurotsuki.Core.Booru;
using Kurotsuki.Core.I18n;

namespace Kurotsuki.App.ViewModels;

/// <summary>A tab that shows posts as a grid of thumbnails: search results, favourites, a folder, a pool.</summary>
/// <summary>A post kept out of a grid by the blacklist, and which blacklisted tags caused it.</summary>
public sealed record HiddenPost(Post Post, IReadOnlyList<string> Tags)
{
    public string TagsText => string.Join(", ", Tags);
}

public enum PostSort
{
    LoadOrder,
    Score,
    Newest,
    Resolution,
}

public abstract partial class PostGridViewModel : TabViewModel
{
    public AppServices Services { get; }
    protected readonly CancellationTokenSource Cts = new();

    protected PostGridViewModel(AppServices services)
    {
        Services = services;
        services.Queue.Items.CollectionChanged += OnQueueChanged;
        services.Favorites.PostsChanged += RefreshStates;
        services.Queue.DownloadedChanged += RefreshStates;
        _tileSize = Math.Clamp(services.Layout.Get("grid.tile", 200), MinTile, MaxTile);
    }

    public const double MinTile = 120;
    public const double MaxTile = 360;

    /// <summary>Thumbnail edge in pixels; shared by every grid and remembered with the window layout.</summary>
    [ObservableProperty] private double _tileSize;

    partial void OnTileSizeChanged(double value) => Services.Layout.Set("grid.tile", Math.Round(value));

    public ObservableCollection<PostViewModel> Posts { get; } = [];

    [ObservableProperty] private PostSort _sort = PostSort.LoadOrder;

    private int _sequence;

    partial void OnSortChanged(PostSort value) => ApplySort();

    /// <summary>Puts the loaded posts in the chosen order; <see cref="PostSort.LoadOrder"/> restores the order they arrived in.</summary>
    private void ApplySort()
    {
        var ordered = (Sort switch
        {
            PostSort.Score => Posts.OrderByDescending(p => p.Post.Score).ThenBy(p => p.Sequence),
            PostSort.Newest => Posts.OrderByDescending(p => p.Post.Id).ThenBy(p => p.Sequence),
            PostSort.Resolution => Posts.OrderByDescending(p => (long)p.Post.Width * p.Post.Height).ThenBy(p => p.Sequence),
            _ => Posts.OrderBy(p => p.Sequence),
        }).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            if (!ReferenceEquals(Posts[i], ordered[i]))
            {
                Posts.Move(Posts.IndexOf(ordered[i]), i);
            }
        }
    }

    /// <summary>Loaded posts that the rating filter lets through; what the viewer, "Queue all" and selection act on.</summary>
    public IEnumerable<PostViewModel> ShownPosts => Posts.Where(p => p.Shown);

    // Filter for the posts already loaded (and the ones that load later); every rating is shown by default.
    [ObservableProperty] private bool _showSafe = true;
    [ObservableProperty] private bool _showQuestionable = true;
    [ObservableProperty] private bool _showExplicit = true;

    partial void OnShowSafeChanged(bool value) => ApplyRatingFilter();
    partial void OnShowQuestionableChanged(bool value) => ApplyRatingFilter();
    partial void OnShowExplicitChanged(bool value) => ApplyRatingFilter();

    private void ApplyRatingFilter()
    {
        foreach (var post in Posts)
        {
            post.Shown = IsShown(post);
        }
    }

    private bool IsShown(PostViewModel post) =>
        post.PassesRating(ShowSafe, ShowQuestionable, ShowExplicit) && PassesText(post.Post, FilterText);

    // Ctrl+Shift+F shows a box that narrows the loaded posts by tag: every word must match one of the post's tags, "-word" must not.
    [ObservableProperty] private bool _filterVisible;
    [ObservableProperty] private string _filterText = "";

    partial void OnFilterTextChanged(string value) => ApplyRatingFilter();

    partial void OnFilterVisibleChanged(bool value)
    {
        if (!value)
        {
            FilterText = "";
        }
    }

    public static bool PassesText(Post post, string? filter)
    {
        var words = (filter ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return true;
        }
        var tags = post.TagList.ToList();
        bool Has(string word) => tags.Any(t => t.Contains(word.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase));
        return words.All(w => w.StartsWith('-') && w.Length > 1 ? !Has(w[1..]) : Has(w));
    }

    /// <summary>Vertical scroll offset, kept up to date by the view so "Save state" can store it.</summary>
    public double ScrollOffset { get; set; }

    /// <summary>An offset restored from a saved session that the view has not been able to apply yet (the grid was too short).</summary>
    public double PendingScroll { get; set; }

    /// <summary>The position to store: a restored one the view has not reached yet counts, so an unvisited tab keeps it.</summary>
    public double SavedScroll => PendingScroll > 0 ? PendingScroll : ScrollOffset;

    /// <summary>Whether <see cref="LoadNextPageAsync"/> can still bring more posts.</summary>
    public virtual bool HasMore => false;

    /// <summary>Posts the blacklist kept out of this grid, with the tags that matched (see the blacklist report).</summary>
    public ObservableCollection<HiddenPost> Hidden { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _showEmpty;
    [ObservableProperty] private string _emptyText = Messages.Get("search.noResults");

    /// <summary>Called when the user scrolls near the bottom; grids that load everything at once ignore it.</summary>
    public virtual Task LoadNextPageAsync() => Task.CompletedTask;

    protected void AddPosts(IEnumerable<Post> posts)
    {
        foreach (var post in posts)
        {
            var vm = new PostViewModel(post, Services) { Owner = this };
            vm.RefreshState();
            vm.Shown = IsShown(vm);
            vm.Sequence = _sequence++;
            Posts.Add(vm);
            _ = vm.LoadThumbnailAsync(Cts.Token);
        }
        if (Sort != PostSort.LoadOrder)
        {
            ApplySort();
        }
    }

    protected void ClearPosts() => Posts.Clear();

    /// <summary>The user kept a hidden post: it joins the grid and will not be hidden again.</summary>
    public void Restore(HiddenPost hidden)
    {
        Services.BlacklistAllowed.Allow(hidden.Post.Key);
        Hidden.Remove(hidden);
        AddPosts([hidden.Post]);
        ShowEmpty = false;
        OnHiddenChanged();
    }

    /// <summary>Called when <see cref="Hidden"/> changed, so the status line and the report button follow.</summary>
    protected virtual void OnHiddenChanged() => OnPropertyChanged(nameof(HasHidden));

    public bool HasHidden => Hidden.Count > 0;

    [RelayCommand]
    private void ReviewHidden() => Services.Navigator.OpenBlacklistReport(this);

    public void OpenViewer(PostViewModel post)
    {
        var shown = ShownPosts.ToList();
        var index = shown.IndexOf(post);
        if (index >= 0)
        {
            Services.Navigator.OpenViewer(shown.Select(p => p.Post).ToList(), index);
        }
    }

    public void AddToQueue(IEnumerable<PostViewModel> posts)
    {
        var added = Services.Queue.AddAll(posts.Select(p => p.Post));
        Status = added == 0 ? Messages.Get("next.nothingNew") : Messages.Get("next.addedToQueue", added);
    }

    [RelayCommand]
    private void AddAllLoaded() => AddToQueue(ShownPosts);

    private void OnQueueChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshStates();

    private void RefreshStates()
    {
        foreach (var post in Posts)
        {
            post.RefreshState();
        }
    }

    public override void Dispose()
    {
        Services.Queue.Items.CollectionChanged -= OnQueueChanged;
        Services.Favorites.PostsChanged -= RefreshStates;
        Services.Queue.DownloadedChanged -= RefreshStates;
        Cts.Cancel();
        Cts.Dispose();
        base.Dispose();
    }
}
