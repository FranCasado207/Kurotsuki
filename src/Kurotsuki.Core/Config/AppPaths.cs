namespace Kurotsuki.Core.Config;

/// <summary>
/// Where Kurotsuki keeps its files. Mirrors the JavaFX app: XDG base directories on Linux, and
/// <c>~/kurotsuki</c> (cache in <c>.cache</c> below it) on Windows and macOS.
/// </summary>
public sealed record AppPaths(string Config, string Data, string Cache)
{
    private const string AppFolder = "kurotsuki";
    private static readonly Lazy<AppPaths> Current = new(() =>
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var paths = Resolve(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), Environment.GetEnvironmentVariable, home);
        MigrateLegacy(Path.Combine(home, AppFolder), paths);
        return paths;
    });

    public static AppPaths Get() => Current.Value;

    public static AppPaths Resolve(bool windows, bool mac, Func<string, string?> env, string home)
    {
        if (windows || mac)
        {
            var folder = Path.Combine(home, AppFolder);
            return new AppPaths(folder, folder, Path.Combine(folder, ".cache"));
        }
        return new AppPaths(
            Path.Combine(Xdg(env, "XDG_CONFIG_HOME", Path.Combine(home, ".config")), AppFolder),
            Path.Combine(Xdg(env, "XDG_DATA_HOME", Path.Combine(home, ".local", "share")), AppFolder),
            Path.Combine(Xdg(env, "XDG_CACHE_HOME", Path.Combine(home, ".cache")), AppFolder));
    }

    private static string Xdg(Func<string, string?> env, string variable, string fallback)
    {
        var value = env(variable);
        // The spec says relative values must be ignored.
        return !string.IsNullOrWhiteSpace(value) && Path.IsPathRooted(value) ? value : fallback;
    }

    public string DatabaseFile => Path.Combine(Data, "kurotsuki.db");

    /// <summary>
    /// Versions before XDG kept everything in <c>~/kurotsuki</c>. If that folder exists and the new location has no
    /// config yet, move the database, the cache and finally config.json over (its presence marks the move as complete).
    /// </summary>
    public static void MigrateLegacy(string legacy, AppPaths target)
    {
        if (!Directory.Exists(legacy) || Path.GetFullPath(legacy) == Path.GetFullPath(target.Config)
            || File.Exists(Path.Combine(target.Config, "config.json")))
        {
            return;
        }
        try
        {
            Directory.CreateDirectory(target.Config);
            Directory.CreateDirectory(target.Data);
            foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
            {
                MoveIfPresent(Path.Combine(legacy, "kurotsuki.db" + suffix), Path.Combine(target.Data, "kurotsuki.db" + suffix));
            }
            var oldCache = Path.Combine(legacy, ".cache");
            if (Directory.Exists(oldCache) && Path.GetFullPath(oldCache) != Path.GetFullPath(target.Cache) && !Directory.Exists(target.Cache))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target.Cache))!);
                Directory.Move(oldCache, target.Cache);
            }
            MoveIfPresent(Path.Combine(legacy, "config.json"), Path.Combine(target.Config, "config.json"));
            if (!Directory.EnumerateFileSystemEntries(legacy).Any())
            {
                Directory.Delete(legacy);
            }
        }
        catch (IOException e)
        {
            Logging.AppLog.Warning($"Could not move data from {legacy} to the new locations", e);
        }
    }

    private static void MoveIfPresent(string from, string to)
    {
        if (File.Exists(from) && !File.Exists(to))
        {
            File.Move(from, to);
        }
    }
}
