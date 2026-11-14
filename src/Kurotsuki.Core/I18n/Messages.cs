using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Kurotsuki.Core.I18n;

/// <summary>
/// Message lookup backed by the Java app's <c>messages*.properties</c> bundles (embedded resources), so keys,
/// <c>{0}</c> placeholders and translations carry over. Missing keys fall back to English, then to the key itself.
/// </summary>
public static partial class Messages
{
    private static readonly Dictionary<string, string> English = Load("messages.properties");
    private static Dictionary<string, string> _current = English;

    public static string Language { get; private set; } = "en";

    /// <summary>Takes the config value (<c>en</c>, <c>pt</c>, <c>pt-PT</c>…); anything unknown means English.</summary>
    public static void SetLanguage(string? language)
    {
        var code = (language ?? "").Trim().ToLowerInvariant();
        code = code.Split('-', '_')[0];
        if (code is "" or "en" || Load($"messages_{code}.properties") is not { Count: > 0 } bundle)
        {
            Language = "en";
            _current = English;
            return;
        }
        Language = code;
        _current = bundle;
    }

    public static string Get(string key, params object?[] args)
    {
        if (!_current.TryGetValue(key, out var pattern) && !English.TryGetValue(key, out pattern))
        {
            return key;
        }
        return args.Length == 0 ? Unquote(pattern) : Format(pattern, args);
    }

    /// <summary>Java <c>MessageFormat</c> rules, reduced to what the bundles use: <c>{n}</c> and <c>''</c> / <c>'…'</c> quoting.</summary>
    internal static string Format(string pattern, object?[] args)
    {
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '\'')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '\'')
                {
                    sb.Append('\'');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (c == '{' && !quoted && Placeholder().Match(pattern, i) is { Success: true, Index: var at } m && at == i)
            {
                var index = int.Parse(m.Groups[1].Value);
                sb.Append(index < args.Length ? Convert.ToString(args[index], CultureInfo.CurrentCulture) : m.Value);
                i += m.Length - 1;
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static string Unquote(string pattern) => pattern.Contains('\'') ? pattern.Replace("''", "'") : pattern;

    private static Dictionary<string, string> Load(string resource)
    {
        var assembly = typeof(Messages).Assembly;
        var name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(resource, StringComparison.Ordinal));
        if (name is null)
        {
            return [];
        }
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return ParseProperties(reader.ReadToEnd());
    }

    /// <summary>The subset of the .properties format the bundles use: <c>key=value</c>, <c>#</c> comments, <c>\uXXXX</c>, <c>\n</c>, line continuation.</summary>
    internal static Dictionary<string, string> ParseProperties(string text)
    {
        var result = new Dictionary<string, string>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimStart();
            if (line.Length == 0 || line[0] is '#' or '!')
            {
                continue;
            }
            while (line.EndsWith('\\') && !line.EndsWith("\\\\") && i + 1 < lines.Length)
            {
                line = line[..^1] + lines[++i].TrimStart();
            }
            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }
            result[line[..eq].Trim()] = Unescape(line[(eq + 1)..].TrimStart());
        }
        return result;
    }

    private static string Unescape(string value)
    {
        if (!value.Contains('\\'))
        {
            return value;
        }
        var sb = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length)
            {
                sb.Append(value[i]);
                continue;
            }
            var next = value[++i];
            switch (next)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'u' when i + 4 < value.Length + 0 && int.TryParse(value.AsSpan(i + 1, 4), NumberStyles.HexNumber, null, out var code):
                    sb.Append((char)code);
                    i += 4;
                    break;
                default: sb.Append(next); break;
            }
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex Placeholder();
}
