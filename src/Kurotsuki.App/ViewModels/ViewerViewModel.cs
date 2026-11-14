using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kurotsuki.App.Services;
using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Config;
using Kurotsuki.Core.Download;
using Kurotsuki.Core.I18n;
using Kurotsuki.Core.Library;
using Kurotsuki.Core.Logging;
using Kurotsuki.Core.Media;

namespace Kurotsuki.App.ViewModels;

/// <summary>One tag in the viewer's tag panel.</summary>
public sealed partial class TagItemViewModel(string tag, TagCategory category) : ObservableObject
{
    public string Tag { get; } = tag;
    public TagCategory Category { get; } = category;
    public IBrush Brush { get; } = TagColors.BrushOf(category);
    public string Display => Tag.Replace('_', ' ');

    [ObservableProperty] private bool _isFavorite;
}

public sealed partial class ViewerViewModel : ObservableObject, IDisposable
{
    private static readonly HashSet<string> ImageTypes = ["png", "jpg", "jpeg", "bmp", "webp", "gif", "ico"];
    private static readonly HashSet<string> VideoTypes = ["mp4", "webm", "mkv", "mov", "avi", "m4v", "ogv", "flv"];
    private const double MaxZoom = 8;
    private const double MinZoom = 0.1;
    private const double ZoomStep = 1.25;

    private readonly AppServices _services;
    private readonly IReadOnlyList<Post> _posts;
    private CancellationTokenSource _load = new();
    private string? _currentFile;
    private AnimationPlayer? _animation;

    public ViewerViewModel(AppServices services, IReadOnlyList<Post> posts, int index)
    {
        _services = services;
        _posts = posts;
        _services.Favorites.Tags.CollectionChanged += (_, _) => RefreshTagFavorites();
        _services.Favorites.PostsChanged += RefreshPostState;
        _index = Math.Clamp(index, 0, posts.Count - 1);
        _post = posts[_index];
        _ = LoadAsync();
    }

    public ObservableCollection<TagItemViewModel> Tags { get; } = [];
    public ObservableCollection<Pool> Pools { get; } = [];
    public int Count => _posts.Count;
    public string Title => Messages.Get("viewer.title", Post.Id);
    public string Position => $"{Index + 1} / {Count}";
    public bool CanGoBack => Index > 0;
    public bool CanGoForward => Index < Count - 1;
    public bool HasPoolsFeature => Booru is not null && SupportsPools;
    public string PoolsLabel => Pools.Count > 0 ? Messages.Get("pools.viewerButtonCount", Pools.Count) : Messages.Get("pools.viewerButton");
    public string FavoriteLabel => IsFavorite ? Messages.Get("common.unfavorite") : Messages.Get("common.favorite");
    public BooruConfiguration? Booru => _services.FindBooru(Post.BooruBaseUrl);
    public ObservableCollection<string> FolderNames => _services.Folders.Names;

    private bool SupportsPools
    {
        get
        {
            try { return Booru is { } b && _services.Clients.ClientFor(b).SupportsPools; }
            catch (Exception) { return false; }
        }
    }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Title))] [NotifyPropertyChangedFor(nameof(Booru))]
    private Post _post;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Position))] [NotifyPropertyChangedFor(nameof(CanGoBack))] [NotifyPropertyChangedFor(nameof(CanGoForward))]
    private int _index;

    [ObservableProperty] private Bitmap? _image;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasVideo))] private VideoViewModel? _video;
    [ObservableProperty] private bool _isAnimated;

    public bool HasVideo => Video is not null;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private bool _canOpenExternally;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FavoriteLabel))] private bool _isFavorite;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ZoomText))] [NotifyPropertyChangedFor(nameof(DisplayWidth))] [NotifyPropertyChangedFor(nameof(DisplayHeight))]
    private double _zoom = 1;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DisplayWidth))] [NotifyPropertyChangedFor(nameof(DisplayHeight))] [NotifyPropertyChangedFor(nameof(ImageStretch))]
    private bool _fitToWindow = true;

    public string ZoomText => FitToWindow ? Messages.Get("next.fit") : $"{Math.Round(Zoom * 100)}%";
    // Fill, not None: with an explicit Width/Height, Stretch.None draws the bitmap at its natural size and merely clips it.
    // Width and Height keep the aspect ratio, so Fill never distorts.
    public Stretch ImageStretch => FitToWindow ? Stretch.Uniform : Stretch.Fill;

    /// <summary>Supplied by the window: the scale at which the image currently fits the viewport.</summary>
    public Func<double>? FitScale { get; set; }
    public double DisplayWidth => FitToWindow || Image is null ? double.NaN : Image.Size.Width * Zoom;
    public double DisplayHeight => FitToWindow || Image is null ? double.NaN : Image.Size.Height * Zoom;

    partial void OnImageChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(DisplayWidth));
        OnPropertyChanged(nameof(DisplayHeight));
    }


    // ---- navigation -----------------------------------------------------------------------------------------

    [RelayCommand]
    private void Next() => Go(Index + 1);

    [RelayCommand]
    private void Previous() => Go(Index - 1);

    private void Go(int index)
    {
        if (index < 0 || index >= Count || index == Index)
        {
            return;
        }
        Index = index;
        Post = _posts[index];
        _ = LoadAsync();
    }

    // ---- loading --------------------------------------------------------------------------------------------

    private async Task LoadAsync()
    {
        _load.Cancel();
        _load.Dispose();
        _load = new CancellationTokenSource();
        var ct = _load.Token;
        var post = Post;

        StopMedia();
        Image = null;
        Error = "";
        CanOpenExternally = false;
        _currentFile = null;
        Status = "";
        Zoom = 1;
        FitToWindow = true;
        IsFavorite = _services.Favorites.IsPostFavorite(post);
        Pools.Clear();
        OnPropertyChanged(nameof(PoolsLabel));
        OnPropertyChanged(nameof(HasPoolsFeature));
        BuildTags(post);
        _ = LoadCategoriesAsync(post, ct);
        _ = LoadPoolsAsync(post, ct);

        var url = post.FileUrl ?? post.SampleUrl;
        if (url is null)
        {
            Error = Messages.Get("viewer.noFile");
            return;
        }
        IsLoading = true;
        try
        {
            var path = await _services.Cache.FetchAsync(url, ct);
            _currentFile = path;
            var ext = ImageSniffer.DetectExtension(path, ExtensionOf(post));
            if (VideoTypes.Contains(ext))
            {
                StartVideo(path, ext);
                return;
            }
            if (ext == "zip") // ugoira: a zip of frames; anything else fails with a clear message
            {
                await StartUgoiraAsync(path, ct);
                return;
            }
            if (!ImageTypes.Contains(ext))
            {
                Error = Messages.Get("viewer.noViewer", ext);
                CanOpenExternally = Shell.IsOpenable(ext);
                return;
            }
            if (ext == "gif" && !ct.IsCancellationRequested)
            {
                StartGif(path);
                return;
            }
            if (ext is "webp" or "png" && !ct.IsCancellationRequested && await Task.Run(() => IsAnimatedImage(path, ext), ct))
            {
                StartSkiaAnimation(path);
                return;
            }
            var bitmap = await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                return new Bitmap(stream);
            }, ct);
            if (!ct.IsCancellationRequested)
            {
                Image = bitmap;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Error = e is IOException or HttpRequestException ? Messages.Get("viewer.loadError", e.Message) : Messages.Get("viewer.imageError");
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }

    private void StartGif(string path)
    {
        IsAnimated = true;
        _animation = new AnimationPlayer(() => GifDecoder.Frames(path),
            frame => { Image = frame; IsLoading = false; },
            error => FallBackToStill(path, error));
    }

    private static bool IsAnimatedImage(string path, string ext) =>
        ext == "png" ? ApngDecoder.IsAnimated(path) : SkiaAnimation.IsAnimated(path);

    private IEnumerable<AnimationFrame> FramesOf(string path) =>
        ApngDecoder.IsAnimated(path) ? ApngDecoder.Frames(path, _services.FrameDecoder) : SkiaAnimation.Frames(path);

    private void StartSkiaAnimation(string path)
    {
        IsAnimated = true;
        _animation = new AnimationPlayer(() => FramesOf(path),
            frame => { Image = frame; IsLoading = false; },
            error => FallBackToStill(path, error));
    }

    /// <summary>The GIF could not be decoded as an animation: show its first frame like any image, or say why not.</summary>
    private void FallBackToStill(string path, string reason)
    {
        IsAnimated = false;
        try
        {
            using var stream = File.OpenRead(path);
            Image = new Bitmap(stream);
        }
        catch (Exception)
        {
            Error = Messages.Get("next.animationError", reason);
        }
        IsLoading = false;
    }

    private async Task StartUgoiraAsync(string path, CancellationToken ct)
    {
        List<Ugoira.Frame> frames;
        try
        {
            frames = await Task.Run(() => Ugoira.Read(path), ct);
        }
        catch (Exception e) when (e is IOException or InvalidDataException)
        {
            Error = Messages.Get("next.animationError", e.Message);
            CanOpenExternally = Shell.IsOpenable("zip");
            return;
        }
        IsAnimated = true;
        _animation = new AnimationPlayer(() => Ugoira.Frames(frames, _services.FrameDecoder),
            frame => { Image = frame; IsLoading = false; },
            error => { IsAnimated = false; Error = Messages.Get("next.animationError", error); IsLoading = false; });
    }

    private void StartVideo(string path, string ext)
    {
        try
        {
            Video = new VideoViewModel(path);
        }
        catch (VideoPlayback.VideoUnavailableException e)
        {
            Error = Messages.Get("next.videoFailed", e.Message);
            CanOpenExternally = Shell.IsOpenable(ext);
        }
    }

    private void StopMedia()
    {
        _animation?.Dispose();
        _animation = null;
        Video?.Dispose();
        Video = null;
        IsAnimated = false;
    }

    [RelayCommand]
    private Task Retry() => LoadAsync();

    private static string ExtensionOf(Post post)
    {
        var dot = post.Image?.LastIndexOf('.') ?? -1;
        return dot < 0 ? "" : post.Image![(dot + 1)..].ToLowerInvariant();
    }

    // ---- tags -----------------------------------------------------------------------------------------------

    private void BuildTags(Post post)
    {
        Tags.Clear();
        foreach (var tag in post.TagList.Distinct().OrderBy(t => TagColors.Order(post.CategoryOf(t))).ThenBy(t => t))
        {
            Tags.Add(new TagItemViewModel(tag, post.CategoryOf(tag)) { IsFavorite = _services.Favorites.IsTagFavorite(tag) });
        }
    }

    private async Task LoadCategoriesAsync(Post post, CancellationToken ct)
    {
        try
        {
            if (Booru is not { } booru)
            {
                return;
            }
            await TagCategoryResolver.ResolveAsync(post, _services.Clients.ClientFor(booru), _services.Store, ct);
            if (!ct.IsCancellationRequested)
            {
                BuildTags(post);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            SetStatus(Messages.Get("viewer.categoriesError", e.Message), true);
        }
    }

    private async Task LoadPoolsAsync(Post post, CancellationToken ct)
    {
        try
        {
            if (!SupportsPools)
            {
                return;
            }
            var pools = await _services.Clients.ClientFor(Booru!).PoolsOfAsync(post, ct);
            if (ct.IsCancellationRequested)
            {
                return;
            }
            Pools.Clear();
            foreach (var pool in pools)
            {
                Pools.Add(pool);
            }
            OnPropertyChanged(nameof(PoolsLabel));
        }
        catch (Exception)
        {
            // the Pools button simply stays without a count
        }
    }

    private void RefreshTagFavorites()
    {
        foreach (var tag in Tags)
        {
            tag.IsFavorite = _services.Favorites.IsTagFavorite(tag.Tag);
        }
    }

    private void RefreshPostState() => IsFavorite = _services.Favorites.IsPostFavorite(Post);

    // ---- actions --------------------------------------------------------------------------------------------

    public void SearchTag(string tag) => _services.Navigator.SearchSelected(tag);

    public void ToggleTagFavorite(TagItemViewModel tag) => _services.Favorites.ToggleTag(tag.Tag);

    [RelayCommand]
    private void ToggleFavorite() => _services.Favorites.TogglePost(Post);

    [RelayCommand]
    private void OpenBrowser()
    {
        var url = BooruHttp.PageUrl(Booru?.EffectiveEngine, Booru?.EffectiveFlavor, Post.BooruBaseUrl, Post.Id);
        if (!Shell.OpenUrl(url))
        {
            SetStatus(Messages.Get("viewer.browserError", Messages.Get("next.noPostPage")), true);
        }
    }

    [RelayCommand]
    private void OpenExternal()
    {
        // The cache has no extensions, so hand the desktop a copy that has one.
        if (_currentFile is null)
        {
            return;
        }
        try
        {
            var ext = ImageSniffer.DetectExtension(_currentFile, ExtensionOf(Post));
            var copy = Path.Combine(_services.Cache.Folder(), "open", $"{Path.GetFileName(_currentFile)}.{ext}");
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(_currentFile, copy, overwrite: true);
            if (!Shell.OpenFile(copy))
            {
                SetStatus(Messages.Get("viewer.openError", ext), true);
            }
        }
        catch (IOException e)
        {
            SetStatus(Messages.Get("viewer.openError", e.Message), true);
        }
    }

    [RelayCommand]
    private void Download()
    {
        var folder = _services.Config.DownloadFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            SetStatus(Messages.Get("download.folderNotSet"), true);
            return;
        }
        if (!_services.Queue.Contains(Post.Key))
        {
            _services.Queue.Toggle(Post);
        }
        if (_services.Queue.Items.FirstOrDefault(i => i.Key == Post.Key) is { Status: DownloadStatus.Queued or DownloadStatus.Failed } item)
        {
            item.Status = DownloadStatus.Queued;
            _services.Queue.Start(item);
        }
        SetStatus(Messages.Get("next.addedToQueue", 1), false);
    }

    public void AddToFolder(string folder)
    {
        _services.Folders.Add(folder, Post);
        SetStatus($"{Messages.Get("grid.addToFolder")}: {folder}", false);
    }

    public void OpenPool(Pool pool)
    {
        if (Booru is { } booru)
        {
            _services.Navigator.OpenPool(pool, booru);
        }
    }

    // ---- zoom -----------------------------------------------------------------------------------------------

    public void ZoomBy(double factor)
    {
        if (FitToWindow && Image is not null)
        {
            // Start from what the fitted image looks like, so the first step does not jump.
            Zoom = Math.Max(FitScale?.Invoke() ?? 1, MinZoom); // a tiny image fits at a scale above MaxZoom; do not snap it down
            FitToWindow = false;
        }
        Zoom = Math.Clamp(Zoom * factor, MinZoom, Math.Max(MaxZoom, (FitScale?.Invoke() ?? 1) * 4));
    }

    [RelayCommand] private void ZoomIn() => ZoomBy(ZoomStep);
    [RelayCommand] private void ZoomOut() => ZoomBy(1 / ZoomStep);

    [RelayCommand]
    private void ActualSize()
    {
        Zoom = 1;
        FitToWindow = false;
    }

    [RelayCommand]
    private void Fit()
    {
        Zoom = 1;
        FitToWindow = true;
    }

    private void SetStatus(string message, bool isError)
    {
        Status = message;
        StatusIsError = isError;
    }

    // ---- slideshow (P): moves to the next post every few seconds and stops at the last one ------------------------------

    public const int SlideshowSeconds = 5;
    private Avalonia.Threading.DispatcherTimer? _slideshow;

    [ObservableProperty] private bool _slideshowOn;

    partial void OnSlideshowOnChanged(bool value)
    {
        _slideshow?.Stop();
        _slideshow = null;
        if (value)
        {
            _slideshow = new Avalonia.Threading.DispatcherTimer(TimeSpan.FromSeconds(SlideshowSeconds), Avalonia.Threading.DispatcherPriority.Background,
                (_, _) => SlideshowTick());
            _slideshow.Start();
        }
    }

    /// <summary>One step of the slideshow. A video is left to play instead of being cut off.</summary>
    public void SlideshowTick()
    {
        if (!SlideshowOn || Video is { IsPlaying: true })
        {
            return;
        }
        if (CanGoForward)
        {
            Go(Index + 1);
        }
        else
        {
            SlideshowOn = false;
        }
    }

    public void Dispose()
    {
        SlideshowOn = false;
        _services.Favorites.PostsChanged -= RefreshPostState;
        StopMedia();
        _load.Cancel();
        _load.Dispose();
    }
}
