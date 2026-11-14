using System.Text.Json;
using Kurotsuki.Core.Logging;

namespace Kurotsuki.Core.Library;

/// <summary>
/// The recent searches, newest first. Kept in its own small file next to the database: the JavaFX app has no search history,
/// so config.json and kurotsuki.db stay exactly as it expects them.
/// </summary>
public sealed class SearchHistory
{
    public const int Limit = 30;
    private readonly string _file;
    private readonly List<string> _items = [];

    public SearchHistory(string file)
    {
        _file = file;
        try
        {
            if (File.Exists(file))
            {
                _items.AddRange(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file))?
                    .Where(s => !string.IsNullOrWhiteSpace(s)).Take(Limit) ?? []);
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            AppLog.Warning("Could not read the search history", e);
        }
    }

    public event Action? Changed;

    public IReadOnlyList<string> Items => _items;

    /// <summary>Records a search. The same query (ignoring case and extra spaces) moves to the top instead of repeating.</summary>
    public void Add(string? query)
    {
        var normalized = string.Join(' ', (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0)
        {
            return;
        }
        _items.RemoveAll(s => s.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        _items.Insert(0, normalized);
        if (_items.Count > Limit)
        {
            _items.RemoveRange(Limit, _items.Count - Limit);
        }
        Save();
    }

    public void Clear()
    {
        _items.Clear();
        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var temp = _file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_items));
            File.Move(temp, _file, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Warning("Could not save the search history", e);
        }
        Changed?.Invoke();
    }
}
