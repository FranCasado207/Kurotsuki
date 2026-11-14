using Kurotsuki.App.Services;
using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Config;
using Kurotsuki.Core.I18n;
using Kurotsuki.Core.Logging;

namespace Kurotsuki.App.ViewModels;

public sealed class SearchTabViewModel : PostGridViewModel
{
    /// <summary>Posts per request; same as the JavaFX app.</summary>
    public const int PageSize = 100;

    private int _nextPage;
    private bool _exhausted;

    public SearchTabViewModel(AppServices services, BooruConfiguration booru, string? tags) : base(services)
    {
        Booru = booru;
        Tags = string.IsNullOrWhiteSpace(tags) ? null : tags.Trim();
        Title = Tags ?? booru.KindLabel;
        ShowExplicit = !services.Prefs.IsSafeMode(booru.Identity);
    }

    protected override void OnHiddenChanged()
    {
        base.OnHiddenChanged();
        Status = Hidden.Count > 0 ? Messages.Get("search.hidden", Hidden.Count) : "";
    }

    public override bool HasMore => !_exhausted;

    public BooruConfiguration Booru { get; }
    public string? Tags { get; }

    public override async Task LoadNextPageAsync()
    {
        if (IsLoading || _exhausted || Cts.IsCancellationRequested)
        {
            return;
        }
        IsLoading = true;
        var page = _nextPage;
        Status = page > 0 ? Messages.Get("search.loadingMore") : "";
        try
        {
            var client = Services.Clients.ClientFor(Booru);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var received = await client.SearchPostsAsync(Tags, page, PageSize, Cts.Token);
            AppLog.Info($"Search ok: {Booru.EffectiveEngine} {AppLog.SafeUrl(Booru.BaseUrl)} page={page} results={received.Count} ms={clock.ElapsedMilliseconds}");
            _nextPage = page + 1;
            _exhausted = received.Count < PageSize;

            var blocked = new HashSet<string>(Services.Config.BlacklistedTags);
            var visible = new List<Post>();
            foreach (var post in received)
            {
                if (TagBlacklist.IsBlocked(post, blocked, Services.BlacklistAllowed))
                {
                    Hidden.Add(new HiddenPost(post, TagBlacklist.MatchingTags(post, blocked)));
                }
                else
                {
                    visible.Add(post);
                }
            }
            AddPosts(visible);
            OnHiddenChanged();
            ShowEmpty = page == 0 && Posts.Count == 0;
            if (_exhausted && Posts.Count > 0)
            {
                Status = $"{Status}  {Messages.Get("search.end")}".Trim();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            AppLog.Warning($"Search failed: {Booru.EffectiveEngine} {AppLog.SafeUrl(Booru.BaseUrl)} page={page}", e);
            Status = Messages.Get("search.error", e.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }
}
