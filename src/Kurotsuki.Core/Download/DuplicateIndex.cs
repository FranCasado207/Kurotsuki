using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Logging;

namespace Kurotsuki.Core.Download;

/// <summary>
/// Remembers the md5 of every file downloaded, so the same picture offered by another booru is not saved twice. The md5 is the file
/// name boorus use (<c>&lt;md5&gt;.&lt;ext&gt;</c>); posts without one are never treated as duplicates. Kept in a plain text file next to
/// the data, because the database schema belongs to the JavaFX app as well.
/// </summary>
public sealed class DuplicateIndex
{
    private readonly HashSet<string> _hashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _file;

    public DuplicateIndex(string file)
    {
        _file = file;
        try
        {
            if (File.Exists(file))
            {
                _hashes.UnionWith(File.ReadLines(file).Where(l => l.Length == 32));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Warning("Could not read the duplicate index", e);
        }
    }

    public int Count => _hashes.Count;

    /// <summary>The md5 in the post's file name, or null.</summary>
    public static string? HashOf(Post post)
    {
        var name = post.Image is { } image ? Path.GetFileNameWithoutExtension(image) : null;
        return name is { Length: 32 } && name.All(Uri.IsHexDigit) ? name.ToLowerInvariant() : null;
    }

    public bool Contains(Post post) => HashOf(post) is { } h && _hashes.Contains(h);

    public void Add(Post post)
    {
        if (HashOf(post) is not { } hash || !_hashes.Add(hash))
        {
            return;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.AppendAllText(_file, hash + "\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Warning("Could not update the duplicate index", e);
        }
    }

    public void Clear()
    {
        _hashes.Clear();
        try
        {
            File.Delete(_file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
