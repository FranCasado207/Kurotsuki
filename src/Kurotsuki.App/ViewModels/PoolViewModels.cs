using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kurotsuki.App.Services;
using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Config;
using Kurotsuki.Core.I18n;
using Kurotsuki.Core.Storage;

namespace Kurotsuki.App.ViewModels;

/// <summary>One row of the pools list: the pool, its cover and where the user is in it.</summary>
public sealed partial class PoolItemViewModel(Pool pool, BooruConfiguration booru, AppServices services) : ObservableObject
{
    public Pool Pool { get; } = pool;
    public BooruConfiguration Booru { get; } = booru;

    [ObservableProperty] private Bitmap? _cover;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ProgressText))] [NotifyPropertyChangedFor(nameof(HasProgress))]
    private ReadingRecord? _record = services.Reading.Get(pool.Key);

    public string Name => Pool.DisplayName;
    public string Details => $"{Messages.Get("pools.posts", Pool.PostCount)}  ·  {Pool.Category}"
        + (Pool.UpdatedAt is { Length: >= 10 } u ? "  ·  " + Messages.Get("pools.updated", u[..10]) : "");

    public bool HasProgress => Record is not null;
    public string ProgressText => Record switch
    {
        null => "",
        { Finished: true } => Messages.Get("reader.finished"),
        var r => Messages.Get("reading.inProgress", r.Page + 1, Math.Max(r.Total, Pool.PostCount)),
    };

    public void RefreshProgress() => Record = services.Reading.Get(Pool.Key);

    public async Task LoadCoverAsync(Post post, CancellationToken ct)
    {
        var url = post.PreviewUrl ?? post.SampleUrl;
        if (url is null)
        {
            return;
        }
        try
        {
            var path = await services.Cache.FetchAsync(url, ct);
            Cover = await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                return Bitmap.DecodeToWidth(stream, 200);
            }, ct);
        }
        catch (Exception)
        {
            // no cover is fine
        }
    }

    [RelayCommand]
    private void Open() => services.Navigator.OpenPool(Pool, Booru);

    [RelayCommand]
    private void Read() => services.Navigator.OpenReader(Pool, Booru, services.Reading.PageOf(Pool.Key));

    [RelayCommand]
    private void ReadFromStart() => services.Navigator.OpenReader(Pool, Booru, 0);

    [RelayCommand]
    private void Download() => services.Queue.DownloadPool(Pool);

    [RelayCommand]
    private void OpenSite()
    {
        Shell.OpenUrl(BooruHttp.PoolUrl(Booru.EffectiveEngine, Booru.EffectiveFlavor, Pool.BooruBaseUrl, Pool.Id));
    }

    [RelayCommand]
    private void MarkFinished()
    {
        services.Reading.SetFinished(Pool, Record?.Finished != true);
        RefreshProgress();
    }
}

/// <summary>Browse and search the pools of a booru, with covers and reading status.</summary>
public sealed partial class PoolsTabViewModel : TabViewModel
{
    private const int PageSize = 40;
    private readonly AppServices _services;
    private readonly CancellationTokenSource _cts = new();
    private int _nextPage;
    private bool _exhausted;

    public PoolsTabViewModel(AppServices services, BooruConfiguration? booru, string? query = null)
    {
        _services = services;
        Title = Messages.Get("pools.tab");
        Boorus = new ObservableCollection<BooruConfiguration>(services.Config.BooruConfigurations.Where(Supports));
        _selectedBooru = booru is not null && Boorus.Contains(booru) ? booru : Boorus.FirstOrDefault();
        _categories = [Messages.Get("pools.category.all"), Messages.Get("pools.category.series"), Messages.Get("pools.category.collection")];
        _category = _categories[0];
        _orders = Enum.GetValues<PoolOrder>().Select(o => new OrderChoice(o, Messages.Get(o.LabelKey()))).ToList();
        _order = _orders[0];
        _query = query?.Trim() ?? "";
        services.Reading.Entries.CollectionChanged += (_, _) => { foreach (var p in Pools) p.RefreshProgress(); };
        if (_selectedBooru is not null)
        {
            _ = SearchAsync();
        }
        else
        {
            Status = Messages.Get("pools.notSupported");
        }
    }

    public sealed record OrderChoice(PoolOrder Order, string Label)
    {
        public override string ToString() => Label;
    }

    private bool Supports(BooruConfiguration b)
    {
        try
        {
            return _services.Clients.ClientFor(b).SupportsPools;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public ObservableCollection<BooruConfiguration> Boorus { get; }
    public ObservableCollection<PoolItemViewModel> Pools { get; } = [];
    public IReadOnlyList<string> Categories => _categories;
    public IReadOnlyList<OrderChoice> Orders => _orders;
    private readonly List<string> _categories;
    private readonly List<OrderChoice> _orders;

    [ObservableProperty] private BooruConfiguration? _selectedBooru;
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private string _category;
    [ObservableProperty] private OrderChoice _order;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _filtersVisible = true;

    [RelayCommand]
    public async Task SearchAsync()
    {
        Pools.Clear();
        _nextPage = 0;
        _exhausted = false;
        await LoadNextPageAsync();
    }

    public async Task LoadNextPageAsync()
    {
        if (IsLoading || _exhausted || SelectedBooru is null || _cts.IsCancellationRequested)
        {
            return;
        }
        IsLoading = true;
        Status = Messages.Get("pools.loading");
        try
        {
            var client = _services.Clients.ClientFor(SelectedBooru);
            FiltersVisible = client.SupportsPoolFilters;
            var category = Category == _categories[1] ? Pool.Series : Category == _categories[2] ? Pool.Collection : null;
            var page = _nextPage;
            var received = await client.SearchPoolsAsync(string.IsNullOrWhiteSpace(Query) ? null : Query, category,
                Order.Order, page, PageSize, _cts.Token);
            _nextPage = page + 1;
            _exhausted = received.Count < client.PoolPageSize(PageSize);
            var items = received.Select(p => new PoolItemViewModel(p, SelectedBooru, _services)).ToList();
            foreach (var item in items)
            {
                Pools.Add(item);
            }
            Status = Pools.Count == 0 ? Messages.Get("pools.empty") : Messages.Get("pools.count", Pools.Count);
            _ = LoadCoversAsync(client, items);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Status = Messages.Get("pools.error", e.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadCoversAsync(IBooruClient client, List<PoolItemViewModel> items)
    {
        try
        {
            var covers = await client.PoolCoversAsync(items.Select(i => i.Pool).ToList(), _cts.Token);
            foreach (var item in items)
            {
                if (covers.TryGetValue(item.Pool.Id, out var post))
                {
                    await item.LoadCoverAsync(post, _cts.Token);
                }
            }
        }
        catch (Exception)
        {
            // covers are decoration; the list works without them
        }
    }

    public override void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        base.Dispose();
    }
}

/// <summary>The posts of one pool as a grid, with Read and Download.</summary>
public sealed partial class PoolTabViewModel : PostGridViewModel
{
    public PoolTabViewModel(AppServices services, Pool pool, BooruConfiguration booru) : base(services)
    {
        Pool = pool;
        Booru = booru;
        Title = pool.DisplayName;
        _ = LoadAsync();
    }

    public Pool Pool { get; }
    public BooruConfiguration Booru { get; }

    private async Task LoadAsync()
    {
        IsLoading = true;
        Status = Messages.Get("pools.loading");
        try
        {
            var posts = await Services.Clients.ClientFor(Booru).PoolPostsAsync(Pool, Cts.Token);
            AddPosts(posts);
            Status = "";
            ShowEmpty = Posts.Count == 0;
            EmptyText = Messages.Get("pools.postsEmpty");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Status = Messages.Get("pools.error", e.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Read() => Services.Navigator.OpenReader(Pool, Booru, Services.Reading.PageOf(Pool.Key));

    [RelayCommand]
    private void ReadFromStart() => Services.Navigator.OpenReader(Pool, Booru, 0);

    [RelayCommand]
    private void Download() => Services.Queue.DownloadPool(Pool);
}

/// <summary>"Continue reading": the pools the user opened in the reader.</summary>
public sealed partial class ReadingTabViewModel : TabViewModel
{
    private readonly AppServices _services;

    public ReadingTabViewModel(AppServices services)
    {
        _services = services;
        Title = Messages.Get("reading.tab");
        _filters = [Messages.Get("reading.filter.all"), Messages.Get("reading.filter.reading"), Messages.Get("reading.filter.finished")];
        _filter = _filters[0];
        services.Reading.Entries.CollectionChanged += (_, _) => Rebuild();
        Rebuild();
    }

    private readonly List<string> _filters;
    public IReadOnlyList<string> Filters => _filters;
    public ObservableCollection<ReadingItemViewModel> Items { get; } = [];

    [ObservableProperty] private string _filter;
    [ObservableProperty] private string _status = "";

    partial void OnFilterChanged(string value) => Rebuild();

    private void Rebuild()
    {
        Items.Clear();
        foreach (var record in _services.Reading.Entries)
        {
            if (Filter == _filters[1] && record.Finished || Filter == _filters[2] && !record.Finished)
            {
                continue;
            }
            Items.Add(new ReadingItemViewModel(record, this, _services));
        }
        Status = Items.Count == 0 ? Messages.Get("reading.empty") : "";
    }

    internal void Report(string message) => Status = message;
}

public sealed partial class ReadingItemViewModel(ReadingRecord record, ReadingTabViewModel tab, AppServices services) : ObservableObject
{
    public ReadingRecord Record { get; } = record;
    public string Name => Record.Name ?? $"Pool #{Record.PoolId}";
    public string ProgressText => Record.Finished
        ? Messages.Get("reader.finished")
        : Messages.Get("reading.progress", Record.Page + 1, Math.Max(Record.Total, 1));
    public string Host => Uri.TryCreate(Record.BooruBaseUrl, UriKind.Absolute, out var u) ? u.Host : Record.BooruBaseUrl;
    public string FinishToggleText => Record.Finished ? Messages.Get("reading.markReading") : Messages.Get("reading.markFinished");

    [RelayCommand]
    private async Task Continue()
    {
        var booru = services.FindBooru(Record.BooruBaseUrl);
        if (booru is null)
        {
            tab.Report(Messages.Get("reading.noBooru", Record.BooruBaseUrl));
            return;
        }
        try
        {
            var pool = await services.Clients.ClientFor(booru).GetPoolAsync(Record.PoolId)
                ?? throw new InvalidOperationException(Messages.Get("pools.notFound", Record.PoolId));
            services.Navigator.OpenReader(pool, booru, Record.Page);
        }
        catch (Exception e)
        {
            tab.Report(Messages.Get("pools.error", e.Message));
        }
    }

    [RelayCommand]
    private void ToggleFinished() => services.Reading.SetFinished(Record, !Record.Finished);

    [RelayCommand]
    private void Forget() => services.Reading.Remove(Record.PoolKey);
}
