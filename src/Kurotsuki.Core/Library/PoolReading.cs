using System.Collections.ObjectModel;
using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Storage;

namespace Kurotsuki.Core.Library;

/// <summary>
/// Which pools the user is reading or has finished, and where they stopped. A pool counts as finished once its last
/// page was shown, and stays finished until the user resets it.
/// </summary>
public sealed class PoolReading
{
    private readonly LibraryStore _store;
    private readonly Action<string> _onError;
    private readonly Dictionary<string, ReadingRecord> _byKey = [];
    private bool _errorShown;

    public PoolReading(LibraryStore store, Action<string> onError)
    {
        _store = store;
        _onError = onError;
        Reload();
    }

    /// <summary>Newest first.</summary>
    public ObservableCollection<ReadingRecord> Entries { get; } = [];

    public void Reload()
    {
        try
        {
            _byKey.Clear();
            foreach (var record in _store.ReadingRecords())
            {
                _byKey[record.PoolKey] = record;
            }
        }
        catch (Exception e)
        {
            _onError(I18n.Messages.Get("reading.saveError", e.Message));
        }
        Refresh();
    }

    public ReadingRecord? Get(string poolKey) => _byKey.GetValueOrDefault(poolKey);

    /// <summary>The page to resume at; 0 when the pool was never opened.</summary>
    public int PageOf(string poolKey) => _byKey.TryGetValue(poolKey, out var r) ? r.Page : 0;

    public bool IsFinished(string poolKey) => _byKey.TryGetValue(poolKey, out var r) && r.Finished;

    /// <summary>Called as the reader moves through the pool.</summary>
    public void Record(Pool pool, int page, int total)
    {
        _byKey.TryGetValue(pool.Key, out var previous);
        var finished = previous?.Finished == true || (total > 0 && page >= total - 1);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Put(previous?.With(page, total, finished, now) ?? New(pool, page, total, finished, now));
    }

    /// <summary>Marks a pool finished or reading again without touching the page. Creates the entry if needed.</summary>
    public void SetFinished(Pool pool, bool finished)
    {
        _byKey.TryGetValue(pool.Key, out var previous);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Put(previous?.With(previous.Page, Math.Max(previous.Total, pool.PostCount), finished, now)
            ?? New(pool, finished ? Math.Max(0, pool.PostCount - 1) : 0, pool.PostCount, finished, now));
    }

    public void SetFinished(ReadingRecord record, bool finished) =>
        Put(record.With(record.Page, record.Total, finished, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

    /// <summary>Forgets the pool: it is unread again.</summary>
    public void Remove(string poolKey)
    {
        if (!_byKey.Remove(poolKey, out var removed))
        {
            return;
        }
        Refresh();
        StoreWrites.Persist("reading.saveError", ReportOnce, () => _store.DeleteReading(poolKey), () =>
        {
            _byKey[poolKey] = removed;
            Refresh();
        });
    }

    private static ReadingRecord New(Pool pool, int page, int total, bool finished, long now) =>
        new(pool.Key, pool.BooruBaseUrl ?? "", pool.Id, pool.DisplayName, pool.Category, page, total, finished, now);

    private void Put(ReadingRecord record)
    {
        _byKey.TryGetValue(record.PoolKey, out var previous);
        _byKey[record.PoolKey] = record;
        Refresh();
        StoreWrites.Persist("reading.saveError", ReportOnce, () => _store.SaveReading(record), () =>
        {
            if (previous is null) _byKey.Remove(record.PoolKey); else _byKey[record.PoolKey] = previous;
            Refresh();
        });
    }

    /// <summary>Progress is saved on every page turn, so a broken database must not open a dialog each time.</summary>
    private void ReportOnce(string message)
    {
        if (!_errorShown)
        {
            _errorShown = true;
            _onError(message);
        }
    }

    private void Refresh()
    {
        Entries.Clear();
        foreach (var record in _byKey.Values.OrderByDescending(r => r.UpdatedAt))
        {
            Entries.Add(record);
        }
    }
}
