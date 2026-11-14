using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Kurotsuki.App.Services;
using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Config;
using Kurotsuki.Core.I18n;

namespace Kurotsuki.App.ViewModels;

public sealed partial class PostViewModel(Post post, AppServices services) : ObservableObject
{
    private const int ThumbnailWidth = 240;
    private const int HoverTags = 12;
    private static readonly SemaphoreSlim ThumbnailSlots = new(6);

    public Post Post { get; } = post;

    /// <summary>The grid that shows this post; lets menus open the viewer with the grid's other posts as context.</summary>
    public PostGridViewModel? Owner { get; set; }

    /// <summary>Arrival order in its grid, so a sort can be undone.</summary>
    public int Sequence { get; set; }

    [ObservableProperty] private Bitmap? _thumbnail;
    [ObservableProperty] private bool _thumbnailFailed;
    [ObservableProperty] private bool _inQueue;
    [ObservableProperty] private bool _wasDownloaded;
    [ObservableProperty] private bool _isFavorite;

    /// <summary>"queued" only while the download is still to come; a finished one just shows "saved".</summary>
    public bool ShowQueued => InQueue && !WasDownloaded;

    partial void OnInQueueChanged(bool value) => OnPropertyChanged(nameof(ShowQueued));
    partial void OnWasDownloadedChanged(bool value) => OnPropertyChanged(nameof(ShowQueued));

    public string Caption => $"#{Post.Id}  ·  {Post.Width}×{Post.Height}  ·  ★{Post.Score}";

    // Hover card (see PostGridView): header facts, then the first tags as chips.
    public IReadOnlyList<string> HoverTagList => [.. Post.TagList.Take(HoverTags).Select(t => t.Replace('_', ' '))];
    public string HoverMore => Post.TagList.Count() is var n && n > HoverTags ? Messages.Get("hover.moreTags", n - HoverTags) : "";
    public bool HasHoverTags => Post.TagList.Any();
    public string ScoreText => $"★ {Post.Score}";
    public string Dimensions => Post.Width > 0 ? $"{Post.Width}×{Post.Height}" : "";
    public string IdText => $"#{Post.Id}";
    public string RatingText => (Post.Rating ?? "").ToLowerInvariant() switch
    {
        "general" or "safe" or "s" or "g" => Messages.Get("hover.ratingSafe"),
        "questionable" or "sensitive" or "q" => Messages.Get("hover.ratingQuestionable"),
        "explicit" or "e" => Messages.Get("hover.ratingExplicit"),
        var other => other,
    };
    /// <summary>False while the grid's rating filter hides this post (the tile collapses; the post stays loaded).</summary>
    [ObservableProperty] private bool _shown = true;

    /// <summary>Posts with no known rating are never filtered out.</summary>
    public bool PassesRating(bool safe, bool questionable, bool explicitRating) => RatingKind switch
    {
        0 => safe,
        1 => questionable,
        2 => explicitRating,
        _ => true,
    };

    public bool RatingSafe => RatingKind == 0;
    public bool RatingQuestionable => RatingKind == 1;
    public bool RatingExplicit => RatingKind == 2;
    public bool HasRating => RatingText.Length > 0;

    private int RatingKind => (Post.Rating ?? "").ToLowerInvariant() switch
    {
        "general" or "safe" or "s" or "g" => 0,
        "questionable" or "sensitive" or "q" => 1,
        "explicit" or "e" => 2,
        _ => -1,
    };

    public BooruConfiguration? Booru => services.FindBooru(Post.BooruBaseUrl);

    public void RefreshState()
    {
        InQueue = services.Queue.Contains(Post.Key);
        WasDownloaded = services.Queue.WasDownloaded(Post.Key);
        IsFavorite = services.Favorites.IsPostFavorite(Post);
    }

    public async Task LoadThumbnailAsync(CancellationToken ct)
    {
        var url = Post.PreviewUrl ?? Post.SampleUrl;
        if (url is null)
        {
            ThumbnailFailed = true;
            return;
        }
        await ThumbnailSlots.WaitAsync(ct);
        try
        {
            var path = await services.Cache.FetchAsync(url, ct);
            Thumbnail = await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                return Bitmap.DecodeToWidth(stream, ThumbnailWidth);
            }, ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            ThumbnailFailed = true; // not an image Skia can decode, or the download failed
        }
        finally
        {
            ThumbnailSlots.Release();
        }
    }

    public void ToggleQueue()
    {
        services.Queue.Toggle(Post);
        RefreshState();
    }

    public void ToggleFavorite()
    {
        services.Favorites.TogglePost(Post);
        RefreshState();
    }

    public void OpenPage()
    {
        var booru = Booru;
        var url = BooruHttp.PageUrl(booru?.EffectiveEngine, booru?.EffectiveFlavor, Post.BooruBaseUrl, Post.Id);
        if (!Shell.OpenUrl(url))
        {
            services.ReportError(Messages.Get("next.noPostPage"));
        }
    }

    public void OpenSource()
    {
        if (!Shell.OpenUrl(Post.Source))
        {
            services.ReportError(Messages.Get("next.noSource"));
        }
    }

    public Task CopyTags() => Shell.CopyToClipboardAsync(Post.Tags ?? "");
    public Task CopyFileUrl() => Shell.CopyToClipboardAsync(Post.FileUrl ?? "");
}
