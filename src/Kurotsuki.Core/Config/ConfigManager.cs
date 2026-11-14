using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kurotsuki.Core.Config;

/// <summary>Loads and saves <c>config.json</c> in the same location and format as the JavaFX app.</summary>
public sealed class ConfigManager(AppPaths paths)
{
    private const string ConfigFile = "config.json";
    private const string TempFile = "config.json.tmp";

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) },
    };

    public string FilePath => Path.Combine(paths.Config, ConfigFile);

    public UserConfiguration Load()
    {
        Directory.CreateDirectory(paths.Config);
        if (!File.Exists(FilePath))
        {
            var fresh = new UserConfiguration();
            Save(fresh);
            return fresh;
        }
        using var stream = File.OpenRead(FilePath);
        var config = JsonSerializer.Deserialize<UserConfiguration>(stream, Json) ?? new UserConfiguration();
        return Sanitize(config);
    }

    private static UserConfiguration Sanitize(UserConfiguration config)
    {
        config.BooruConfigurations ??= [];
        config.BooruConfigurations.RemoveAll(booru =>
        {
            if (booru is null || booru.EffectiveEngine is null)
            {
                return true; // unknown type: the Java app ignores these too
            }
            try
            {
                booru.Validate();
                return false;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        });
        config.BlacklistedTags ??= [];
        config.FavoriteTags ??= [];
        config.FavoritePosts ??= [];
        config.PostFolders ??= [];
        config.PanelDocks ??= [];
        config.PanelSizes ??= [];
        return config;
    }

    public void Save(UserConfiguration config)
    {
        Directory.CreateDirectory(paths.Config);
        RestrictToOwner(paths.Config);
        var temp = Path.Combine(paths.Config, TempFile);
        File.Delete(temp);
        // The file holds API keys: create it owner-only from the start instead of tightening it afterwards.
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }
        using (var stream = new FileStream(temp, options))
        {
            JsonSerializer.Serialize(stream, config, Json);
        }
        File.Move(temp, FilePath, overwrite: true);
    }

    public static void RestrictToOwner(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        try
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
