using System.Text.Json;
using Kurotsuki.Core.Logging;

namespace Kurotsuki.Core.Theming;

/// <summary>
/// Built-in themes plus the user's own, which are <c>*.json</c> files in <c>themes/</c> next to config.json. The chosen theme is kept in
/// <c>next-ui.json</c> rather than config.json, which the JavaFX app shares and must not gain fields it does not know.
/// </summary>
public sealed class ThemeStore(string configDirectory)
{
    private string ThemesDirectory => Path.Combine(configDirectory, "themes");
    private string SelectionFile => Path.Combine(configDirectory, "next-ui.json");

    public string Folder => ThemesDirectory;

    /// <summary>Built-ins first, then custom themes by name. A custom theme cannot shadow a built-in name.</summary>
    public IReadOnlyList<ThemeDefinition> All()
    {
        var list = new List<ThemeDefinition>(ThemeDefinition.BuiltIns);
        if (!Directory.Exists(ThemesDirectory))
        {
            return list;
        }
        var custom = new List<ThemeDefinition>();
        foreach (var file in Directory.EnumerateFiles(ThemesDirectory, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var theme = ThemeDefinition.Parse(File.ReadAllText(file));
                if (list.Concat(custom).All(t => !t.Name.Equals(theme.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    custom.Add(theme);
                }
            }
            catch (Exception e) when (e is IOException or JsonException or FormatException or UnauthorizedAccessException)
            {
                AppLog.Warning($"Ignoring theme file {Path.GetFileName(file)}", e);
            }
        }
        list.AddRange(custom.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase));
        return list;
    }

    public ThemeDefinition Find(string? name) =>
        All().FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? ThemeDefinition.Dark;

    public string? SelectedName()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(SelectionFile));
            return doc.RootElement.TryGetProperty("theme", out var p) ? p.GetString() : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>When on, Dark or Light is picked from the operating system's setting instead of the chosen theme.</summary>
    public bool FollowsSystem()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(SelectionFile));
            return doc.RootElement.TryGetProperty("followSystem", out var p) && p.ValueKind == JsonValueKind.True;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Select(string name) => Write(name, FollowsSystem());

    public void SetFollowSystem(bool follow) => Write(SelectedName() ?? ThemeDefinition.Dark.Name, follow);

    private void Write(string name, bool follow)
    {
        Directory.CreateDirectory(configDirectory);
        var temp = SelectionFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new { theme = name, followSystem = follow }));
        File.Move(temp, SelectionFile, true);
    }

    /// <summary>Saves a custom theme as <c>themes/&lt;slug&gt;.json</c>. Built-in names are refused.</summary>
    public string Save(ThemeDefinition theme)
    {
        if (ThemeDefinition.BuiltIns.Any(t => t.Name.Equals(theme.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("The built-in themes cannot be replaced; pick another name.");
        }
        Directory.CreateDirectory(ThemesDirectory);
        var path = Path.Combine(ThemesDirectory, Slug(theme.Name) + ".json");
        File.WriteAllText(path, theme.ToJson());
        return path;
    }

    public void Delete(string name)
    {
        var path = Path.Combine(ThemesDirectory, Slug(name) + ".json");
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    internal static string Slug(string name)
    {
        var chars = name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = new string(chars).Trim('-');
        return slug.Length == 0 ? "theme" : slug;
    }
}
