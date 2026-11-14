using System.Globalization;
using System.Text.Json;

namespace Kurotsuki.Core.Theming;

/// <summary>
/// A colour theme: a base (light or dark, which decides how the stock controls render) and a handful of colour tokens.
/// Custom themes are small JSON files, so a missing token falls back to the base theme's value.
/// </summary>
public sealed record ThemeDefinition(string Name, bool IsDark, IReadOnlyDictionary<string, string> Colors, bool BuiltIn = false)
{
    public const string DarkName = "Dark";
    public const string LightName = "Light";

    /// <summary>Every token a theme may set. Order is the order shown in the editor.</summary>
    public static readonly IReadOnlyList<string> Tokens =
        ["background", "surface", "raised", "border", "text", "textMuted", "accent", "onAccent", "danger", "success"];

    // Built-in palettes. Pairs are checked for contrast in ThemeTests; keep them readable when editing.
    public static ThemeDefinition Dark { get; } = new("Dark", true, new Dictionary<string, string>
    {
        ["background"] = "#0B0B10",
        ["surface"] = "#13131A",
        ["raised"] = "#1C1C26",
        ["border"] = "#2B2B3A",
        ["text"] = "#EDEDF4",
        ["textMuted"] = "#A0A0B8",
        ["accent"] = "#A78BFA",
        ["onAccent"] = "#1A0B3D",
        ["danger"] = "#FF7B8A",
        ["success"] = "#5EE6A8",
    }, true);

    public static ThemeDefinition Light { get; } = new("Light", false, new Dictionary<string, string>
    {
        ["background"] = "#F5F3EF",
        ["surface"] = "#FFFFFF",
        ["raised"] = "#ECE9E3",
        ["border"] = "#D6D1C7",
        ["text"] = "#1C1B22",
        ["textMuted"] = "#5E5A66",
        ["accent"] = "#4F46E5",
        ["onAccent"] = "#FFFFFF",
        ["danger"] = "#BE123C",
        ["success"] = "#047857",
    }, true);

    public static ThemeDefinition Kuro { get; } = new("Kuro", true, new Dictionary<string, string>
    {
        ["background"] = "#000000",
        ["surface"] = "#0A0A0A",
        ["raised"] = "#151515",
        ["border"] = "#2A2A2A",
        ["text"] = "#F2F2F2",
        ["textMuted"] = "#A3A3A3",
        ["accent"] = "#FBBF24",
        ["onAccent"] = "#1F1500",
        ["danger"] = "#FB7185",
        ["success"] = "#4ADE80",
    }, true);

    public static ThemeDefinition Frost { get; } = new("Frost", true, new Dictionary<string, string>
    {
        ["background"] = "#0F1722",
        ["surface"] = "#152030",
        ["raised"] = "#1D2B3F",
        ["border"] = "#2C3E57",
        ["text"] = "#E6EEF8",
        ["textMuted"] = "#9FB3CC",
        ["accent"] = "#38BDF8",
        ["onAccent"] = "#04202E",
        ["danger"] = "#FB7185",
        ["success"] = "#34D399",
    }, true);

    public static ThemeDefinition Moss { get; } = new("Moss", true, new Dictionary<string, string>
    {
        ["background"] = "#10140F",
        ["surface"] = "#161C15",
        ["raised"] = "#1F281E",
        ["border"] = "#2E3B2C",
        ["text"] = "#E8EFE6",
        ["textMuted"] = "#A4B3A0",
        ["accent"] = "#A3E635",
        ["onAccent"] = "#1A2605",
        ["danger"] = "#FCA5A5",
        ["success"] = "#86EFAC",
    }, true);

    public static ThemeDefinition Sakura { get; } = new("Sakura", false, new Dictionary<string, string>
    {
        ["background"] = "#FFF5F7",
        ["surface"] = "#FFFFFF",
        ["raised"] = "#FCE7EC",
        ["border"] = "#F2CAD3",
        ["text"] = "#3A1F2B",
        ["textMuted"] = "#7A5463",
        ["accent"] = "#D6336C",
        ["onAccent"] = "#FFFFFF",
        ["danger"] = "#B42318",
        ["success"] = "#15803D",
    }, true);

    /// <summary>The themes that ship with the app, in the order they are listed.</summary>
    public static IReadOnlyList<ThemeDefinition> BuiltIns { get; } = [Dark, Light, Kuro, Frost, Moss, Sakura];

    public static bool IsHexColor(string? value)
    {
        if (value is not { Length: 7 or 9 } || value[0] != '#')
        {
            return false;
        }
        return int.TryParse(value.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _)
            || (value.Length == 9 && long.TryParse(value.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _));
    }

    /// <summary>The colour for a token, filled in from the base theme when the theme does not set it.</summary>
    public string Color(string token) =>
        Colors.TryGetValue(token, out var value) && IsHexColor(value) ? value : Base(IsDark).Colors[token];

    public static ThemeDefinition Base(bool dark) => dark ? Dark : Light;

    // ---- JSON ---------------------------------------------------------------------------------------------

    private sealed class Json
    {
        public string? Name { get; set; }
        public string? Base { get; set; }
        public Dictionary<string, string>? Colors { get; set; }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Parses a theme file. Unknown tokens and malformed colours are dropped, not fatal; a missing name is an error.</summary>
    public static ThemeDefinition Parse(string json)
    {
        var raw = JsonSerializer.Deserialize<Json>(json, Options) ?? throw new FormatException("Empty theme file.");
        var name = raw.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            throw new FormatException("A theme needs a name.");
        }
        var colors = new Dictionary<string, string>();
        foreach (var token in Tokens)
        {
            var match = raw.Colors?.FirstOrDefault(p => string.Equals(p.Key, token, StringComparison.OrdinalIgnoreCase));
            if (match is { Value: { } value } && IsHexColor(value.Trim()))
            {
                colors[token] = value.Trim().ToUpperInvariant();
            }
        }
        return new ThemeDefinition(name, !string.Equals(raw.Base, "light", StringComparison.OrdinalIgnoreCase), colors);
    }

    public string ToJson() => JsonSerializer.Serialize(new Json
    {
        Name = Name,
        Base = IsDark ? "dark" : "light",
        Colors = Tokens.ToDictionary(t => t, Color),
    }, Options);
}
