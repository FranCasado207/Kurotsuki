namespace Kurotsuki.Core.Library;

/// <summary>
/// Keeps the decoded pages of the reader within a memory budget. Pages are dropped least recently used first, except
/// the ones the reader is showing or about to show.
/// </summary>
public sealed class PageBudget(long maxBytes)
{
    private readonly LinkedList<int> _lru = new(); // least recently used first
    private readonly Dictionary<int, long> _sizes = [];

    public long Total { get; private set; }

    /// <summary>Decoded size of an image: four bytes per pixel.</summary>
    public static long Estimate(double width, double height) => (long)Math.Ceiling(width) * (long)Math.Ceiling(height) * 4;

    public void Add(int page, long bytes)
    {
        if (_sizes.TryGetValue(page, out var previous))
        {
            Total -= previous;
            _lru.Remove(page);
        }
        _sizes[page] = bytes;
        Total += bytes;
        _lru.AddLast(page);
    }

    /// <summary>Marks the page as recently used.</summary>
    public void Touch(int page)
    {
        if (_sizes.ContainsKey(page))
        {
            _lru.Remove(page);
            _lru.AddLast(page);
        }
    }

    public void Remove(int page)
    {
        if (_sizes.Remove(page, out var size))
        {
            Total -= size;
            _lru.Remove(page);
        }
    }

    /// <summary>Drops pages until the budget fits again and returns them. Protected pages are never dropped.</summary>
    public List<int> Evict(ISet<int> protectedPages)
    {
        var dropped = new List<int>();
        if (Total <= maxBytes)
        {
            return dropped;
        }
        foreach (var page in _lru.ToList())
        {
            if (Total <= maxBytes)
            {
                break;
            }
            if (!protectedPages.Contains(page))
            {
                Remove(page);
                dropped.Add(page);
            }
        }
        return dropped;
    }
}
