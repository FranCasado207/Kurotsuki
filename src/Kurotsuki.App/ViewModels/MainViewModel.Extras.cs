using CommunityToolkit.Mvvm.ComponentModel;
using Kurotsuki.App.Services;
using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Config;
using Kurotsuki.Core.I18n;
using Kurotsuki.Core.Logging;
using Kurotsuki.Core.Session;

namespace Kurotsuki.App.ViewModels;

// Things the search box and menus offer that are not part of the basic shell: pasted post links, watched searches, privacy mode.
public sealed partial class MainViewModel
{
    /// <summary>Blurs every thumbnail (F9), for when the screen is seen by others.</summary>
    [ObservableProperty] private bool _privacyMode;

    // ---- pasted post links and ids ---------------------------------------------------------------------------

    /// <summary>Opens the post a pasted link or id names in the viewer. Returns false when the text is not a post link.</summary>
    private bool TryOpenPostLink(string text)
    {
        if (PostLink.Parse(text) is not { } link)
        {
            return false;
        }
        var booru = link.Host is null
            ? SelectedBooru
            : Boorus.FirstOrDefault(b => Uri.TryCreate(b.BaseUrl, UriKind.Absolute, out var u)
                && string.Equals(u.Host, link.Host, StringComparison.OrdinalIgnoreCase));
        if (booru is null)
        {
            Status = link.Host is null ? Messages.Get("next.chooseBooru") : Messages.Get("next.linkNoBooru", link.Host);
            return true;
        }
        _ = OpenPostByIdAsync(booru, link.Id);
        return true;
    }

    public async Task OpenPostByIdAsync(BooruConfiguration booru, long id)
    {
        Status = Messages.Get("next.openingPost", id);
        try
        {
            var found = await _services.Clients.ClientFor(booru).SearchPostsAsync($"id:{id}", 0, 1);
            if (found.FirstOrDefault(p => p.Id == id) is { } post)
            {
                Status = "";
                OpenViewer([post], 0);
            }
            else
            {
                Status = Messages.Get("next.postNotFound", id);
            }
        }
        catch (Exception e)
        {
            AppLog.Warning($"Could not open post {id}", e);
            Status = Messages.Get("search.error", e.Message);
        }
    }

    // ---- watched searches --------------------------------------------------------------------------------------

    /// <summary>New posts found by the last check, by watched-search name.</summary>
    public Dictionary<string, int> WatchedNews { get; } = [];

    private readonly Dictionary<string, long> _watchedLatest = [];

    public void WatchCurrentSearch()
    {
        var text = SearchText.Trim();
        if (text.Length == 0 || SelectedBooru is null)
        {
            return;
        }
        _services.Prefs.Watch(new WatchedSearch(text, SelectedBooru.Identity, text));
        Status = Messages.Get("next.watchStarted", text);
        _ = CheckWatchedAsync();
    }

    public void StopWatching(string name)
    {
        _services.Prefs.Unwatch(name);
        WatchedNews.Remove(name);
    }

    public void OpenWatched(WatchedSearch watched)
    {
        if (FindBooru(watched.Booru) is { } booru)
        {
            SelectedBooru = booru;
        }
        if (_watchedLatest.TryGetValue(watched.Name, out var latest))
        {
            _services.Prefs.MarkSeen(watched.Name, latest);
        }
        WatchedNews.Remove(watched.Name);
        SearchSelected(watched.Tags);
    }

    /// <summary>
    /// Asks each watched search for its newest posts and counts those above the id seen last time. The first check only records
    /// where the search stands, so nothing is reported as new that was already there.
    /// </summary>
    public async Task CheckWatchedAsync()
    {
        var reports = new List<string>();
        foreach (var watched in _services.Prefs.Watched.ToList())
        {
            if (FindBooru(watched.Booru) is not { } booru)
            {
                continue;
            }
            try
            {
                var posts = await _services.Clients.ClientFor(booru).SearchPostsAsync(watched.Tags, 0, 40);
                var latest = posts.Count == 0 ? 0 : posts.Max(p => p.Id);
                _watchedLatest[watched.Name] = Math.Max(latest, watched.LastSeenId);
                if (watched.LastSeenId == 0)
                {
                    _services.Prefs.MarkSeen(watched.Name, latest);
                    continue;
                }
                var fresh = posts.Count(p => p.Id > watched.LastSeenId);
                if (fresh > 0)
                {
                    WatchedNews[watched.Name] = fresh;
                    reports.Add(Messages.Get("next.watchNew", fresh, watched.Name));
                }
            }
            catch (Exception e)
            {
                AppLog.Warning($"Watched search '{watched.Name}' could not be checked", e);
            }
        }
        if (reports.Count > 0)
        {
            Status = string.Join("  ", reports);
        }
    }
}
