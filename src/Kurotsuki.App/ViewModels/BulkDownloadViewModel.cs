using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kurotsuki.App.Services;
using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Config;
using Kurotsuki.Core.Download;
using Kurotsuki.Core.I18n;
using Kurotsuki.Core.Net;

namespace Kurotsuki.App.ViewModels;

/// <summary>"Download search results": queue the posts of pages N..M of the selected search.</summary>
public sealed partial class BulkDownloadViewModel(AppServices services, SearchTabViewModel search) : ObservableObject
{
    private CancellationTokenSource? _cts;

    public string Header => Messages.Get("bulk.header", search.Tags ?? search.Booru.KindLabel);
    public string Hint => Messages.Get("bulk.hint", SearchTabViewModel.PageSize, BulkFetch.MaxPosts, BulkFetch.MaxPages);

    [ObservableProperty] private decimal _fromPage = 1;
    [ObservableProperty] private decimal _toPage = 5;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(StartCommand))] private bool _isRunning;
    [ObservableProperty] private string _status = "";

    /// <summary>Raised when the work finished, so the window can close or stay for the message.</summary>
    public event Action? Finished;

    private bool CanStart => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task Start()
    {
        var first = Math.Max(1, (int)FromPage) - 1;
        var last = Math.Max(first + 1, (int)ToPage) - 1;
        IsRunning = true;
        Status = Messages.Get("bulk.working");
        _cts = new CancellationTokenSource();
        try
        {
            var client = services.Clients.ClientFor(search.Booru);
            var blocked = new HashSet<string>(services.Config.BlacklistedTags);
            var host = Uri.TryCreate(search.Booru.BaseUrl, UriKind.Absolute, out var uri) ? uri.Host : "";
            var total = Math.Min(last, first + BulkFetch.MaxPages - 1) - first + 1;
            var progress = new Progress<int>(read => Status = Messages.Get("bulk.reading", read, total));
            var result = await BulkFetch.FetchAsync(
                page => client.SearchPostsAsync(search.Tags, page, SearchTabViewModel.PageSize),
                first, last, SearchTabViewModel.PageSize, BulkFetch.MaxPosts,
                post => !TagBlacklist.IsBlocked(post, blocked, services.BlacklistAllowed),
                new HostThrottle(BulkFetch.PageSpacing), host, progress, _cts.Token);

            var added = services.Queue.AddAll(result.Posts);
            var message = Messages.Get("bulk.done", added, result.Posts.Count - added);
            if (result.Truncated)
            {
                message += "  " + Messages.Get("bulk.truncated", BulkFetch.MaxPosts);
            }
            if (result.Error is not null)
            {
                message += "  " + Messages.Get("bulk.partial", result.Error);
            }
            Status = message;
        }
        catch (Exception e)
        {
            Status = Messages.Get("bulk.partial", e.Message);
        }
        finally
        {
            IsRunning = false;
            _cts = null;
        }
        Finished?.Invoke();
    }

    public void Cancel() => _cts?.Cancel();
}
