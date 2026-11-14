using System.Text.Json;
using Kurotsuki.Core.Logging;

namespace Kurotsuki.Core.Library;

/// <summary>
/// Posts the user reviewed in the blacklist report and decided to keep, even though one of their tags is blacklisted.
/// Kept in its own file (the JavaFX app has no such thing, so config.json and the database stay untouched).
/// </summary>
public sealed class BlacklistExceptions
{
    private readonly string _file;
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

    public BlacklistExceptions(string file)
    {
        _file = file;
        try
        {
            if (File.Exists(file))
            {
                foreach (var key in JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file)) ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        _keys.Add(key);
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            AppLog.Warning("Could not read the blacklist exceptions", e);
        }
    }

    public event Action? Changed;

    public int Count => _keys.Count;

    /// <param name="postKey"><see cref="Booru.Post.Key"/>: booru address plus post id.</param>
    public bool IsAllowed(string postKey) => _keys.Contains(postKey);

    public void Allow(string postKey)
    {
        if (_keys.Add(postKey))
        {
            Save();
        }
    }

    public void Revoke(string postKey)
    {
        if (_keys.Remove(postKey))
        {
            Save();
        }
    }

    public void Clear()
    {
        if (_keys.Count > 0)
        {
            _keys.Clear();
            Save();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var temp = _file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_keys.Order(StringComparer.Ordinal)));
            File.Move(temp, _file, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Warning("Could not save the blacklist exceptions", e);
        }
        Changed?.Invoke();
    }
}
