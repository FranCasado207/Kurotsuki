using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kurotsuki.App.Services;
using Kurotsuki.App.Views;
using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Config;
using Kurotsuki.Core.Download;
using Kurotsuki.Core.I18n;
using Kurotsuki.Core.Library;
using Kurotsuki.Core.Session;

namespace Kurotsuki.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, INavigator
{
    private readonly AppServices _services;
    private readonly HashSet<TagGroup> _watchedGroups = [];
    private ViewerWindow? _viewer;

    public MainViewModel(AppServices services)
    {
        _services = services;
        services.Navigator = this;
        services.ReportError = message => Dispatcher.UIThread.Post(() => Status = message);
        services.Queue.Items.CollectionChanged += OnQueueChanged;
        services.Favorites.Tags.CollectionChanged += (_, _) => RebuildFavorites();
        services.TagGroups.Groups.CollectionChanged += (_, _) => RebuildFavorites();
        ReloadBoorus();
        RebuildFavorites();
        UpdateOverall();
        Status = string.Join("  ", services.StartupMessages);
        _sidebarVisible = services.Layout.Flag("sidebar.visible", false);
        _queueVisible = services.Layout.Flag("queue.visible", false);

        if (services.Session.Load() is { } saved)
        {
            RestoreSession(saved);
        }
        else if (services.Reading.Entries.Any(r => !r.Finished))
        {
            // "Continue reading" opens at start when something is in progress (as in the JavaFX app).
            OpenReading();
        }
        Tabs.CollectionChanged += (_, _) => SaveSession();
        // Watched searches are checked a few seconds after start, so the window is up first.
        if (services.Prefs.Watched.Count > 0)
        {
            _ = Task.Delay(4000).ContinueWith(_ => Dispatcher.UIThread.Post(() => _ = CheckWatchedAsync()));
        }
    }

    partial void OnSelectedTabChanged(TabViewModel? oldValue, TabViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }
        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }
        SaveSession();
    }

    // ---- "Save state" -------------------------------------------------------------------------------------

    private bool _restoring;

    /// <summary>Writes the open tabs, the selected one and the search box, if the user turned "Save state" on. Also called at exit.</summary>
    public void SaveSession()
    {
        if (_restoring)
        {
            return;
        }
        _services.Session.Save(new SessionState(
            Tabs.Select(ToSession).ToList(), Tabs.IndexOf(SelectedTab!), SelectedBooru?.Identity, SearchText, PoolMode));
    }

    private static SessionTab ToSession(TabViewModel tab) => tab switch
    {
        SearchTabViewModel s => new(TabKinds.Search, s.Booru.Identity, s.Tags, Scroll: s.SavedScroll),
        MergedSearchTabViewModel m => new(TabKinds.Merged, Text: m.Tags, Scroll: m.SavedScroll),
        PoolTabViewModel p => new(TabKinds.Pool, p.Booru.Identity, Pool: JsonSerializer.Serialize(p.Pool, ConfigManager.Json),
            Scroll: p.SavedScroll),
        PoolsTabViewModel p => new(TabKinds.Pools, p.SelectedBooru?.Identity, p.Query),
        FolderTabViewModel f => new(TabKinds.Folder, Text: f.Folder, Scroll: f.SavedScroll),
        FavoritesTabViewModel f => new(TabKinds.Favorites, Scroll: f.SavedScroll),
        FoldersTabViewModel => new(TabKinds.Folders),
        _ => new(TabKinds.Reading),
    };

    private BooruConfiguration? FindBooru(string? identity) =>
        identity is null ? null : Boorus.FirstOrDefault(b => b.Identity == identity);

    private void RestoreSession(SessionState saved)
    {
        _restoring = true;
        try
        {
            SelectedBooru = FindBooru(saved.Booru) ?? SelectedBooru;
            SearchText = saved.SearchText;
            PoolMode = saved.PoolMode && CanPools;
            foreach (var entry in saved.Tabs)
            {
                try
                {
                    if (CreateTab(entry) is { } tab)
                    {
                        Tabs.Add(tab);
                    }
                }
                catch (Exception e) when (e is JsonException or InvalidOperationException)
                {
                    // a tab from a booru that is gone, or a pool that no longer parses, is simply not reopened
                }
            }
            SelectedTab = Tabs.ElementAtOrDefault(saved.Selected) ?? Tabs.LastOrDefault();
        }
        finally
        {
            _restoring = false;
        }
    }

    private TabViewModel? CreateTab(SessionTab entry)
    {
        TabViewModel? tab = null;
        switch (entry.Kind)
        {
            case TabKinds.Search when FindBooru(entry.Booru) is { } booru:
                var search = new SearchTabViewModel(_services, booru, entry.Text);
                _ = search.LoadNextPageAsync();
                tab = search;
                break;
            case TabKinds.Merged when CanMerge:
                var merged = new MergedSearchTabViewModel(_services, Boorus, entry.Text);
                _ = merged.LoadNextPageAsync();
                tab = merged;
                break;
            case TabKinds.Pools:
                tab = new PoolsTabViewModel(_services, FindBooru(entry.Booru), entry.Text);
                break;
            case TabKinds.Pool when FindBooru(entry.Booru) is { } booru && entry.Pool is not null
                && JsonSerializer.Deserialize<Pool>(entry.Pool, ConfigManager.Json) is { } pool:
                tab = new PoolTabViewModel(_services, pool, booru);
                break;
            case TabKinds.Favorites:
                tab = new FavoritesTabViewModel(_services);
                break;
            case TabKinds.Folders:
                tab = new FoldersTabViewModel(_services);
                break;
            case TabKinds.Folder when entry.Text is not null && _services.Folders.Names.Contains(entry.Text):
                tab = new FolderTabViewModel(_services, entry.Text);
                break;
            case TabKinds.Reading:
                tab = new ReadingTabViewModel(_services);
                break;
        }
        if (tab is PostGridViewModel grid)
        {
            grid.PendingScroll = entry.Scroll;
        }
        return tab;
    }

    public AppServices Services => _services;

    /// <summary>The open viewer window, if any; the last reader window opened (null before the first).</summary>
    public ViewerWindow? Viewer => _viewer;
    public ReaderWindow? LastReader { get; private set; }
    public ObservableCollection<BooruConfiguration> Boorus { get; } = [];
    public ObservableCollection<TabViewModel> Tabs { get; } = [];
    public ObservableCollection<FavoriteSection> Favorites { get; } = [];
    public ObservableCollection<DownloadItem> QueueItems => _services.Queue.Items;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanBulkDownload))]
    [NotifyPropertyChangedFor(nameof(OnSearch))]
    [NotifyPropertyChangedFor(nameof(OnFavorites))]
    [NotifyPropertyChangedFor(nameof(OnFolders))]
    [NotifyPropertyChangedFor(nameof(OnReading))]
    private TabViewModel? _selectedTab;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanPools))] private BooruConfiguration? _selectedBooru;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SearchPlaceholder))] private bool _poolMode;
    [ObservableProperty] private bool _mergeMode;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _sidebarVisible;
    [ObservableProperty] private bool _queueVisible;
    [ObservableProperty] private string _overallText = "";
    [ObservableProperty] private double _overallProgress;
    [ObservableProperty] private bool _hasQueue;
    [ObservableProperty] private string _queueTitle = Messages.Get("download.queueTitle", 0);

    public string SearchPlaceholder => Messages.Get(PoolMode ? "pools.searchPrompt" : "sidebar.searchPrompt");
    /// <summary>Whether the selected booru has pools; the Pools toggle is hidden otherwise.</summary>
    public bool CanPools
    {
        get
        {
            try
            {
                return SelectedBooru is { } b && _services.Clients.ClientFor(b).SupportsPools;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    partial void OnSelectedBooruChanged(BooruConfiguration? value)
    {
        if (!CanPools)
        {
            PoolMode = false;
        }
    }

    public bool HasBoorus => Boorus.Count > 0;

    /// <summary>The "all boorus" switch only makes sense with at least two.</summary>
    public bool CanMerge => Boorus.Count > 1;
    public bool HasFavorites => Favorites.Count > 0;
    public bool CanBulkDownload => SelectedTab is SearchTabViewModel;

    // Which rail button is lit.
    public bool OnSearch => SelectedTab is SearchTabViewModel or MergedSearchTabViewModel or PoolTabViewModel or PoolsTabViewModel;
    public bool OnFavorites => SelectedTab is FavoritesTabViewModel;
    public bool OnFolders => SelectedTab is FoldersTabViewModel or FolderTabViewModel;
    public bool OnReading => SelectedTab is ReadingTabViewModel;

    [RelayCommand] private void ToggleSidebar() => SidebarVisible = !SidebarVisible;
    [RelayCommand] private void ToggleQueue() => QueueVisible = !QueueVisible;

    partial void OnSidebarVisibleChanged(bool value) => _services.Layout.SetFlag("sidebar.visible", value);
    partial void OnQueueVisibleChanged(bool value) => _services.Layout.SetFlag("queue.visible", value);

    // ---- boorus -------------------------------------------------------------------------------------------

    /// <summary>Called at start and after Settings changed the booru list.</summary>
    public void ReloadBoorus()
    {
        var previous = SelectedBooru;
        Boorus.Clear();
        foreach (var booru in _services.Config.BooruConfigurations)
        {
            Boorus.Add(booru);
        }
        SelectedBooru = Boorus.FirstOrDefault(b => ReferenceEquals(b, previous)) ?? Boorus.FirstOrDefault();
        OnPropertyChanged(nameof(HasBoorus));
        OnPropertyChanged(nameof(CanMerge));
        if (Boorus.Count < 2)
        {
            MergeMode = false;
        }
        if (Boorus.Count == 0)
        {
            Status = Messages.Get("next.noBoorus");
        }
    }

    /// <summary>Suggestions for the last word of the search box, from the selected booru.</summary>
    public async Task<IEnumerable<object>> SuggestAsync(string? text, CancellationToken ct)
    {
        if (PoolMode || SelectedBooru is null || string.IsNullOrWhiteSpace(text) || text.EndsWith(' '))
        {
            return [];
        }
        var last = text[(text.LastIndexOf(' ') + 1)..];
        var negated = last.StartsWith('-');
        var prefix = negated ? last[1..] : last;
        if (prefix.Length < 2)
        {
            return [];
        }
        try
        {
            var names = await _services.Clients.ClientFor(SelectedBooru).SuggestTagsAsync(prefix, 10, ct);
            var head = text[..(text.LastIndexOf(' ') + 1)] + (negated ? "-" : "");
            return names.Select(n => (object)(head + n));
        }
        catch (Exception)
        {
            return []; // autocomplete is a convenience; a failure must not show an error while typing
        }
    }

    // ---- tabs ---------------------------------------------------------------------------------------------

    private T Add<T>(T tab) where T : TabViewModel
    {
        Tabs.Add(tab);
        SelectedTab = tab;
        return tab;
    }

    private T ShowOrAdd<T>(Func<T> create) where T : TabViewModel =>
        Tabs.OfType<T>().FirstOrDefault() is { } existing ? Select(existing) : Add(create());

    private T Select<T>(T tab) where T : TabViewModel
    {
        SelectedTab = tab;
        return tab;
    }

    [RelayCommand]
    private void Search()
    {
        if (!PoolMode && TryOpenPostLink(SearchText))
        {
            return;
        }
        if (SelectedBooru is null)
        {
            Status = Messages.Get("next.chooseBooru");
            return;
        }
        if (PoolMode)
        {
            OpenPools(SelectedBooru, SearchText);
            return;
        }
        if (MergeMode && CanMerge)
        {
            OpenMerged(SearchText);
            return;
        }
        OpenSearch(SelectedBooru, SearchText);
    }

    public void OpenMerged(string? tags)
    {
        _services.History.Add(tags);
        var tab = Add(new MergedSearchTabViewModel(_services, Boorus, tags));
        Status = "";
        _ = tab.LoadNextPageAsync();
    }

    public void OpenSearch(BooruConfiguration booru, string? tags)
    {
        _services.History.Add(tags);
        var tab = Add(new SearchTabViewModel(_services, booru, tags));
        Status = "";
        _ = tab.LoadNextPageAsync();
    }

    /// <summary>Safe mode of the selected booru: its searches start with Explicit posts hidden (the E button brings them back).</summary>
    public void ToggleSafeMode()
    {
        if (SelectedBooru is { } booru)
        {
            var on = !_services.Prefs.IsSafeMode(booru.Identity);
            _services.Prefs.SetSafeMode(booru.Identity, on);
            foreach (var tab in Tabs.OfType<SearchTabViewModel>().Where(t => t.Booru.Identity == booru.Identity))
            {
                tab.ShowExplicit = !on;
            }
        }
    }

    public void SaveCurrentSearch(string name)
    {
        _services.Prefs.SaveSearch(new SavedSearch(name, SelectedBooru?.Identity, SearchText.Trim()));
        Status = Messages.Get("next.searchSaved", name);
    }

    public void RunSavedSearch(SavedSearch search)
    {
        if (FindBooru(search.Booru) is { } booru)
        {
            SelectedBooru = booru;
        }
        SearchSelected(search.Tags);
    }

    public void SearchSelected(string? tags)
    {
        if (SelectedBooru is null)
        {
            Status = Messages.Get("next.chooseBooru");
            return;
        }
        SearchText = tags ?? "";
        OpenSearch(SelectedBooru, tags);
    }

    public void OpenPool(Pool pool, BooruConfiguration booru) => Add(new PoolTabViewModel(_services, pool, booru));
    public void OpenFavorites() => ShowOrAdd(() => new FavoritesTabViewModel(_services));
    public void OpenFolders() => ShowOrAdd(() => new FoldersTabViewModel(_services));
    public void OpenReading() => ShowOrAdd(() => new ReadingTabViewModel(_services));

    public void OpenPools(BooruConfiguration? booru = null) => OpenPools(booru, null);

    public void OpenPools(BooruConfiguration? booru, string? query) =>
        Add(new PoolsTabViewModel(_services, booru ?? SelectedBooru, query));

    public void OpenFolder(string name)
    {
        if (Tabs.OfType<FolderTabViewModel>().FirstOrDefault(t => t.Folder == name) is { } existing)
        {
            Select(existing);
            return;
        }
        Add(new FolderTabViewModel(_services, name));
    }

    [RelayCommand]
    private void CloseTab(TabViewModel? tab)
    {
        if (tab is null)
        {
            return;
        }
        Tabs.Remove(tab);
        tab.Dispose();
    }

    // ---- windows ------------------------------------------------------------------------------------------

    public void OpenBlacklistReport(PostGridViewModel grid)
    {
        var window = new BlacklistReportWindow { DataContext = new BlacklistReportViewModel(grid, _services) };
        if (Shell.MainWindow is { } owner)
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }
    }

    public void OpenViewer(IReadOnlyList<Post> posts, int index)
    {
        var owner = Shell.MainWindow;
        if (_viewer is { } open)
        {
            open.Close(); // one viewer at a time; reopening on the clicked post is what the user expects
        }
        _viewer = new ViewerWindow(_services, new ViewerViewModel(_services, posts, index));
        _viewer.Closed += (_, _) => _viewer = null;
        if (owner is not null)
        {
            _viewer.Show(owner);
        }
        else
        {
            _viewer.Show();
        }
    }

    public void OpenReader(Pool pool, BooruConfiguration booru, int startPage)
    {
        var window = new ReaderWindow(_services, new ReaderViewModel(_services, pool, booru, startPage));
        LastReader = window;
        if (Shell.MainWindow is { } owner)
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }
    }

    // ---- favourite tags (sidebar) -------------------------------------------------------------------------

    private void RebuildFavorites()
    {
        Favorites.Clear();
        var grouped = new HashSet<string>();
        foreach (var group in _services.TagGroups.Groups)
        {
            if (_watchedGroups.Add(group))
            {
                group.Tags.CollectionChanged += OnGroupTagsChanged;
            }
            Favorites.Add(new FavoriteSection(group.Name, group, group.Tags));
            grouped.UnionWith(group.Tags);
        }
        var loose = _services.Favorites.Tags.Where(t => !grouped.Contains(t)).ToList();
        if (loose.Count > 0 || Favorites.Count == 0)
        {
            Favorites.Add(new FavoriteSection("", null, loose));
        }
        OnPropertyChanged(nameof(HasFavorites));
    }

    private void OnGroupTagsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildFavorites();

    [RelayCommand]
    private async Task NewTagGroup()
    {
        var name = await Dialogs.PromptAsync(Shell.MainWindow, Messages.Get("tagGroups.newTitle"), Messages.Get("tagGroups.name"));
        if (name is not null)
        {
            _services.TagGroups.Create(name);
        }
    }

    [RelayCommand]
    private async Task AddFavoriteTag()
    {
        var tag = await Dialogs.PromptAsync(Shell.MainWindow, Messages.Get("next.addToFavorites"), Messages.Get("tagPanel.title"));
        if (tag is not null)
        {
            var normalized = TagBlacklist.Normalize(tag);
            if (!_services.Favorites.IsTagFavorite(normalized))
            {
                _services.Favorites.ToggleTag(normalized);
            }
        }
    }

    // ---- download queue -----------------------------------------------------------------------------------

    private void OnQueueChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            // The downloads drawer is closed by default; adding to it is the moment it is useful.
            QueueVisible = true;
            foreach (DownloadItem item in e.NewItems)
            {
                item.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName is nameof(DownloadItem.Status) or nameof(DownloadItem.Progress))
                    {
                        UpdateOverall();
                    }
                };
            }
        }
        UpdateOverall();
    }

    private void UpdateOverall()
    {
        var items = _services.Queue.Items;
        HasQueue = items.Count > 0;
        var finished = items.Count(i => i.Status is DownloadStatus.Done or DownloadStatus.Skipped);
        OverallProgress = items.Count == 0 ? 0 : items.Sum(i => i.Status is DownloadStatus.Done or DownloadStatus.Skipped ? 1 : i.Progress) / items.Count;
        OverallText = Messages.Get("next.overall", finished, items.Count);
        QueueTitle = Messages.Get("download.queueTitle", items.Count);
    }

    [RelayCommand] private void StartAll() => _services.Queue.StartAll();
    [RelayCommand] private void ClearFinished() => _services.Queue.RemoveFinished();

    [RelayCommand]
    private void ForgetHistory()
    {
        _services.Queue.ForgetHistory();
        Status = Messages.Get("settings.forgotDownloads");
    }

    [RelayCommand]
    private void RemoveItem(DownloadItem? item)
    {
        if (item is not null)
        {
            _services.Queue.Remove(item);
        }
    }

    [RelayCommand]
    private void RetryItem(DownloadItem? item)
    {
        if (item is { Status: DownloadStatus.Failed })
        {
            item.Status = DownloadStatus.Queued;
            _services.Queue.Start(item);
        }
    }

    [RelayCommand]
    private void OpenItemFolder(DownloadItem? item)
    {
        var folder = item?.SavedPath is { } path ? Path.GetDirectoryName(path) : _services.Config.DownloadFolder;
        if (!Shell.OpenFolder(folder))
        {
            Status = Messages.Get("menu.openError", folder ?? "");
        }
    }

    [RelayCommand]
    private void OpenItemFile(DownloadItem? item)
    {
        if (item?.SavedPath is not { } path || !Shell.OpenFile(path))
        {
            Status = Messages.Get("menu.openError", item?.SavedPath ?? "");
        }
    }

    [RelayCommand]
    private void OpenDownloads()
    {
        if (!Shell.OpenFolder(_services.Config.DownloadFolder))
        {
            Status = Messages.Get("download.folderNotSet");
        }
    }

    [RelayCommand]
    private void OpenData()
    {
        if (!Shell.OpenFolder(_services.Paths.Data))
        {
            Status = Messages.Get("menu.openError", _services.Paths.Data);
        }
    }

    // ---- menu: tabs ---------------------------------------------------------------------------------------

    [RelayCommand] private void ShowFavorites() => OpenFavorites();
    [RelayCommand] private void ShowFolders() => OpenFolders();
    [RelayCommand] private void ShowPools() => OpenPools();
    [RelayCommand] private void ShowReading() => OpenReading();
}
