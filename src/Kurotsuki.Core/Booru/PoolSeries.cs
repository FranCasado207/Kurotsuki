using System.Text.RegularExpressions;

namespace Kurotsuki.Core.Booru;

/// <summary>
/// Guesses which pool follows another one in a series from the names alone ("Foo vol. 2" → "Foo vol. 3"): boorus have no real
/// series link, so this only trusts a shared base name plus a higher trailing number.
/// </summary>
public static partial class PoolSeries
{
    [GeneratedRegex(@"^(?<base>.*?)[\s,:\-]*(?:(?:volume|vol|chapter|ch|part|pt|book|bk|episode|ep|no|#)\.?\s*)?[(\[]?(?<n>\d{1,4})[)\]]?\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex Tail();

    /// <summary>The name without its trailing number, and that number; null when the name does not end in one.</summary>
    public static (string Base, int Number)? Parse(string? name)
    {
        var match = Tail().Match((name ?? "").Replace('_', ' ').Trim());
        if (!match.Success || match.Groups["base"].Value.Trim() is not { Length: >= 3 } baseName)
        {
            return null;
        }
        return (baseName, int.Parse(match.Groups["n"].Value));
    }

    /// <summary>The candidate with the same base name and the smallest number above the current one, or null.</summary>
    public static Pool? Next(Pool current, IEnumerable<Pool> candidates)
    {
        if (Parse(current.Name) is not { } now)
        {
            return null;
        }
        return candidates
            .Where(p => p.Id != current.Id && Parse(p.Name) is { } other
                && other.Number > now.Number && other.Base.Equals(now.Base, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => Parse(p.Name)!.Value.Number)
            .FirstOrDefault();
    }

    /// <summary>What to search for when looking for the rest of the series.</summary>
    public static string? SearchTerm(Pool pool) => Parse(pool.Name)?.Base;
}
