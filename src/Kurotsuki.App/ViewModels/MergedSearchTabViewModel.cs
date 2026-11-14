using Kurotsuki.App.Services;
using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Config;
using Kurotsuki.Core.I18n;
using Kurotsuki.Core.Logging;

namespace Kurotsuki.App.ViewModels;

/// <summary>
/// One search sent to every configured booru at once. Each page asks all of them for their next page and interleaves the answers,
/// so the best of each booru comes first. A booru that fails is reported and left out; the others carry on.
/// </summary>
public sealed class MergedSearchTabViewModel : PostGridViewModel
{
    private const int PerBooruPage = 40;

    private sealed class Source(BooruConfiguration booru)
    {
        public BooruConfiguration Booru { get; } = booru;
        public int NextPage;
        public bool Exhausted;
    }

    private readonly List<Source> _sources;
    private readonly HashSet<string> _seen = [];

    public MergedSearchTabViewModel(AppServices services, IEnumerable<BooruConfiguration> boorus, string? tags) : base(services)
    {
        _sources = [.. boorus.Select(b => new Source(b))];
        Tags = string.IsNullOrWhiteSpace(tags) ? null : tags.Trim();
        Title = (Tags ?? Messages.Get("next.mergedTitle")) + " ·  " + _sources.Count;
    }

    public string? Tags { get; }

    public override bool HasMore => _sources.Any(s => !s.Exhausted);

    public override async Task LoadNextPageAsync()
    {
        if (IsLoading || !HasMore || Cts.IsCancellationRequested)
        {
            return;
        }
        IsLoading = true;
        Status = Messages.Get("search.loadingMore");
        try
        {
            var active = _sources.Where(s => !s.Exhausted).ToList();
            var answers = await Task.WhenAll(active.Select(s => FetchAsync(s)));

            var blocked = new HashSet<string>(Services.Config.BlacklistedTags);
            var visible = new List<Post>();
            // Round-robin: first of each booru, then second of each, and so on.
            var longest = answers.Max(a => a.Posts.Count);
            for (var i = 0; i < longest; i++)
            {
                foreach (var (_, posts, _) in answers)
                {
                    if (i >= posts.Count || !_seen.Add(posts[i].Image ?? posts[i].Key))
                    {
                        continue; // the same file (same md5) on another booru is shown once
                    }
                    if (TagBlacklist.IsBlocked(posts[i], blocked, Services.BlacklistAllowed))
                    {
                        Hidden.Add(new HiddenPost(posts[i], TagBlacklist.MatchingTags(posts[i], blocked)));
                    }
                    else
                    {
                        visible.Add(posts[i]);
                    }
                }
            }
            AddPosts(visible);
            OnHiddenChanged();
            ShowEmpty = Posts.Count == 0 && !HasMore;
            var failed = answers.Where(a => a.Error is not null).Select(a => a.Source.Booru.ShortName).ToList();
            Status = failed.Count > 0 ? Messages.Get("next.mergedFailed", string.Join(", ", failed))
                : !HasMore && Posts.Count > 0 ? Messages.Get("search.end")
                : Hidden.Count > 0 ? Messages.Get("search.hidden", Hidden.Count) : "";
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task<(Source Source, IReadOnlyList<Post> Posts, string? Error)> FetchAsync(Source source)
    {
        try
        {
            var client = Services.Clients.ClientFor(source.Booru);
            var received = await client.SearchPostsAsync(Tags, source.NextPage, PerBooruPage, Cts.Token);
            source.NextPage++;
            source.Exhausted = received.Count < PerBooruPage;
            return (source, received, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            // A broken booru must not block the others, and asking it again on every page would only repeat the failure.
            AppLog.Warning($"Merged search failed on {source.Booru.ShortName}", e);
            source.Exhausted = true;
            return (source, [], e.Message);
        }
    }
}
