using System.Text.Json;
using Kurotsuki.Core.Logging;

namespace Kurotsuki.Core.Session;

/// <summary>One open tab. Only the fields its <see cref="Kind"/> needs are set; <see cref="Scroll"/> is the grid's vertical offset.</summary>
public sealed record SessionTab(string Kind, string? Booru = null, string? Text = null, string? Pool = null, double Scroll = 0);

/// <summary>What "Save state" remembers about the main window between runs.</summary>
public sealed record SessionState(
    List<SessionTab> Tabs,
    int Selected = -1,
    string? Booru = null,
    string SearchText = "",
    bool PoolMode = false);

public static class TabKinds
{
    public const string Search = "search";
    public const string Merged = "merged";
    public const string Pools = "pools";
    public const string Pool = "pool";
    public const string Favorites = "favorites";
    public const string Folders = "folders";
    public const string Folder = "folder";
    public const string Reading = "reading";
}

/// <summary>
/// The "Save state" option and the saved state, in <c>next-session.json</c> next to config.json (never inside config.json, which
/// the JavaFX app shares). A file that cannot be read counts as "nothing saved".
/// </summary>
public sealed class SessionStore(string configDirectory)
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private sealed record FileModel(bool Enabled, SessionState? State);

    private string File_ => Path.Combine(configDirectory, "next-session.json");

    /// <summary>Whether the user turned "Save state" on.</summary>
    public bool Enabled => Read()?.Enabled ?? false;

    /// <summary>The saved state; null when the option is off or nothing was saved yet.</summary>
    public SessionState? Load() => Read() is { Enabled: true, State: { } state } ? state : null;

    /// <summary>Turns the option on or off. Turning it off forgets what was saved, so a later switch-on starts clean.</summary>
    public void SetEnabled(bool enabled)
    {
        var current = Read();
        if (current?.Enabled == enabled)
        {
            return;
        }
        Write(new FileModel(enabled, enabled ? current?.State : null));
    }

    /// <summary>Stores the state, if the option is on.</summary>
    public void Save(SessionState state)
    {
        if (Enabled)
        {
            Write(new FileModel(true, state));
        }
    }

    private FileModel? Read()
    {
        try
        {
            return JsonSerializer.Deserialize<FileModel>(File.ReadAllText(File_), Options);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Write(FileModel model)
    {
        try
        {
            Directory.CreateDirectory(configDirectory);
            var temp = File_ + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(model, Options));
            File.Move(temp, File_, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Warning("Could not save the session state", e);
        }
    }
}
