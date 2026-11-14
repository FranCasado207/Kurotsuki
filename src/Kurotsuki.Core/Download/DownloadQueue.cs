using System.Collections.ObjectModel;
using System.Text.Json;
using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Config;
using Kurotsuki.Core.I18n;
using Kurotsuki.Core.Logging;
using Kurotsuki.Core.Net;
using Kurotsuki.Core.Storage;

namespace Kurotsuki.Core.Download;

/// <summary>
/// The download queue. Items and the "downloaded" set are only touched on the thread that created the queue
/// (the UI thread); transfers run on the thread pool and report back through the <c>post</c> callback.
/// </summary>
public sealed class DownloadQueue
{
    private const int ParallelDownloads = 2;
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);

    private readonly IMediaDownloader _downloader;
    private readonly PoolArchiver? _poolArchiver;
    private readonly Func<DownloadSettings> _settings;
    private readonly Func<Post, string> _groupOf;
    private readonly LibraryStore? _store;
    private readonly Action<string> _onError;
    private readonly Action<Action> _post;
    private readonly SemaphoreSlim _slots = new(ParallelDownloads);
    private readonly HashSet<string> _downloaded = [];
    private readonly HashSet<string> _reserved = [];
    private readonly object _inline = new();
    private bool _saveScheduled;

    public ObservableCollection<DownloadItem> Items { get; } = [];

    /// <summary>
    /// Runs on a worker thread after a file was downloaded; may return a different path (ugoira zip → gif). If it throws, the
    /// downloaded file is kept as it is and the error is reported; the download still counts as done.
    /// </summary>
    public Func<string, Post, Task<string>>? PostProcess { get; set; }

    /// <summary>When set, a post whose file (md5) was already downloaded from any booru is skipped instead of saved again.</summary>
    public DuplicateIndex? Duplicates { get; set; }

    /// <summary>Raised when the "already downloaded" knowledge changed (a download finished, or history was cleared), so grids can update their badges.</summary>
    public event Action? DownloadedChanged;

    /// <param name="poolArchiver">Null disables pool downloads.</param>
    /// <param name="groupOf">Names the tag group of a post for the <c>%group%</c> token.</param>
    /// <param name="store">May be null (nothing is remembered then).</param>
    /// <param name="post">Runs an action on the UI thread; defaults to running it inline, serialised (tests).</param>
    public DownloadQueue(IMediaDownloader downloader, PoolArchiver? poolArchiver, Func<DownloadSettings> settings,
        Func<Post, string>? groupOf, LibraryStore? store, Action<string> onError, Action<Action>? post = null)
    {
        _downloader = downloader;
        _poolArchiver = poolArchiver;
        _settings = settings;
        _groupOf = groupOf ?? (_ => "");
        _store = store;
        _onError = onError;
        // Without a UI thread (tests, tools) actions still run one at a time, as they would on a UI thread.
        _post = post ?? (a => { lock (_inline) { a(); } });
        Restore();
        Items.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
            {
                foreach (DownloadItem added in e.NewItems)
                {
                    added.PropertyChanged += (_, args) =>
                    {
                        if (args.PropertyName == nameof(DownloadItem.Status))
                        {
                            SaveQueue();
                        }
                    };
                }
            }
            SaveQueue();
        };
    }

    public bool Contains(string key) => Items.Any(i => i.Key == key);
    public bool WasDownloaded(string key) => _downloaded.Contains(key);

    public void Toggle(Post post)
    {
        if (Items.FirstOrDefault(i => i.Key == post.Key) is { } existing)
        {
            Remove(existing);
        }
        else
        {
            Items.Add(new DownloadItem(post));
        }
    }

    /// <summary>Queues every post that is not queued yet; returns how many were added.</summary>
    public int AddAll(IEnumerable<Post> posts)
    {
        var seen = new HashSet<string>(Items.Select(i => i.Key));
        var added = 0;
        foreach (var post in posts)
        {
            if (seen.Add(post.Key))
            {
                Items.Add(new DownloadItem(post));
                added++;
            }
        }
        return added;
    }

    /// <summary>Adds the pool to the queue and starts saving it right away.</summary>
    public void DownloadPool(Pool pool)
    {
        if (Contains(pool.Key))
        {
            return;
        }
        var current = _settings();
        if (!current.HasFolder)
        {
            _onError(Messages.Get("download.folderNotSet"));
            return;
        }
        var item = new DownloadItem(pool);
        Items.Add(item);
        Start(item, current);
    }

    public void Remove(DownloadItem item)
    {
        if (item.Status != DownloadStatus.Downloading)
        {
            Items.Remove(item);
        }
    }

    public void RemoveFinished()
    {
        foreach (var item in Items.Where(i => i.Status is DownloadStatus.Done or DownloadStatus.Failed
                     or DownloadStatus.Skipped).ToList())
        {
            Items.Remove(item);
        }
    }

    public void StartAll()
    {
        var current = _settings();
        if (!current.HasFolder)
        {
            _onError(Messages.Get("download.folderNotSet"));
            return;
        }
        foreach (var item in Items.Where(i => i.Status == DownloadStatus.Queued).ToList())
        {
            Start(item, current);
        }
    }

    /// <summary>Starts (or retries) a single item.</summary>
    public void Start(DownloadItem item)
    {
        var current = _settings();
        if (!current.HasFolder)
        {
            _onError(Messages.Get("download.folderNotSet"));
            return;
        }
        Start(item, current);
    }

    private void Start(DownloadItem item, DownloadSettings current)
    {
        if (current.SkipKnown && _downloaded.Contains(item.Key))
        {
            item.Status = DownloadStatus.Skipped;
            return;
        }
        if (current.SkipKnown && item.Post is { } candidate && Duplicates?.Contains(candidate) == true)
        {
            item.Message = Messages.Get("next.duplicateSkipped");
            item.Status = DownloadStatus.Skipped;
            return;
        }
        item.Status = DownloadStatus.Downloading;
        item.Progress = 0;
        var directory = current.Folder!;
        // Rendered here, on the UI thread, because the tag groups it may use are UI state.
        var relative = item.Post is null ? null : new FileNameTemplate(current.Template, _groupOf).Render(item.Post);
        var sidecar = current.Sidecar;
        _ = Task.Run(async () =>
        {
            await _slots.WaitAsync();
            string? target = null;
            try
            {
                var lastReport = DateTime.MinValue;
                var progress = new Progress<double>(p =>
                {
                    var now = DateTime.UtcNow;
                    if (p >= 1.0 || now - lastReport > ProgressInterval)
                    {
                        lastReport = now;
                        _post(() => item.Progress = p);
                    }
                });
                if (item.Pool is not null)
                {
                    var archiver = _poolArchiver ?? throw new IOException(Messages.Get("pools.notSupported"));
                    var saved = await archiver.ArchiveAsync(item.Pool, directory, progress);
                    _post(() => Finished(item, saved));
                    return;
                }
                Directory.CreateDirectory(directory);
                target = ReserveTarget(directory, relative!);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await _downloader.DownloadAsync(item.Post!.FileUrl ?? throw new IOException("Post has no file URL"),
                    target, progress, spaced: true);
                var final = target;
                if (PostProcess is { } process)
                {
                    try
                    {
                        final = await process(target, item.Post);
                    }
                    catch (Exception e)
                    {
                        AppLog.Warning("Post-processing failed: " + AppLog.SafeUrl(item.Post.FileUrl), e);
                        _post(() => _onError(Messages.Get("next.postProcessFailed", e.Message)));
                    }
                }
                try
                {
                    await Sidecar.WriteAsync(item.Post, final, sidecar);
                }
                catch (IOException e)
                {
                    _post(() => _onError(Messages.Get("download.sidecarError", e.Message)));
                }
                var path = final;
                _post(() => Finished(item, path));
            }
            catch (Exception e)
            {
                AppLog.Warning("Queue item failed: " + item.Key, e);
                _post(() =>
                {
                    item.Message = string.IsNullOrEmpty(e.Message) ? e.GetType().Name : e.Message;
                    item.Status = DownloadStatus.Failed;
                });
            }
            finally
            {
                Release(target);
                _slots.Release();
            }
        });
    }

    private void Finished(DownloadItem item, string savedPath)
    {
        _downloaded.Add(item.Key);
        if (item.Post is { } saved)
        {
            Duplicates?.Add(saved);
        }
        if (_store is not null)
        {
            var key = item.Key;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            RunStore(() => _store.MarkDownloaded(key, now));
        }
        item.SavedPath = savedPath;
        item.Progress = 1;
        item.Status = DownloadStatus.Done;
        DownloadedChanged?.Invoke();
    }

    /// <summary>Forgets which posts were saved, so they are downloaded again.</summary>
    public void ForgetHistory()
    {
        _downloaded.Clear();
        Duplicates?.Clear();
        if (_store is not null)
        {
            RunStore(_store.ForgetDownloads);
        }
        DownloadedChanged?.Invoke();
    }

    private string ReserveTarget(string directory, string relative)
    {
        lock (_reserved)
        {
            var target = FileNameTemplate.UniqueTarget(directory, relative, _reserved);
            _reserved.Add(target);
            return target;
        }
    }

    private void Release(string? target)
    {
        if (target is not null)
        {
            lock (_reserved)
            {
                _reserved.Remove(target);
            }
        }
    }

    private void Restore()
    {
        if (_store is null)
        {
            return;
        }
        try
        {
            _downloaded.UnionWith(_store.DownloadedKeys());
            foreach (var entry in _store.QueueEntries())
            {
                try
                {
                    DownloadItem? item = entry.Kind switch
                    {
                        "pool" => JsonSerializer.Deserialize<Pool>(entry.Payload, ConfigManager.Json) is { } pool ? new DownloadItem(pool) : null,
                        "post" => JsonSerializer.Deserialize<Post>(entry.Payload, ConfigManager.Json) is { } post ? new DownloadItem(post) : null,
                        _ => null,
                    };
                    if (item is not null)
                    {
                        item.Payload = entry.Payload;
                        Items.Add(item);
                    }
                }
                catch (JsonException)
                {
                    // an entry from a different version that no longer parses is dropped
                }
            }
        }
        catch (Exception e)
        {
            _onError(Messages.Get("download.restoreError", e.Message));
        }
    }

    /// <summary>Remembers what is still to do (everything except finished items) so a restart can pick it up.</summary>
    private void SaveQueue()
    {
        if (_store is null || _saveScheduled)
        {
            return;
        }
        _saveScheduled = true;
        _post(WriteQueue);
    }

    private void WriteQueue()
    {
        _saveScheduled = false;
        var entries = new List<QueueEntry>();
        foreach (var item in Items.Where(i => i.Status is not (DownloadStatus.Done or DownloadStatus.Skipped)))
        {
            item.Payload ??= item.Pool is not null
                ? JsonSerializer.Serialize(item.Pool, ConfigManager.Json)
                : JsonSerializer.Serialize(item.Post, ConfigManager.Json);
            entries.Add(new QueueEntry(item.Key, item.Kind, item.Payload));
        }
        RunStore(() => _store!.SaveQueue(entries));
    }

    private void RunStore(Action write)
    {
        try
        {
            write();
        }
        catch (Exception e)
        {
            _onError(e.Message);
        }
    }
}
