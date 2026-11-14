namespace Kurotsuki.Core.Library;

/// <summary>Groups the pages of a comic into what the reader shows at once: single pages or two-page spreads.</summary>
public static class SpreadLayout
{
    public static List<int[]> Singles(int count) => Enumerable.Range(0, count).Select(i => new[] { i }).ToList();

    /// <summary>
    /// Pairs consecutive pages like an open book. The cover stands alone (when <paramref name="coverAlone"/>), and a wide
    /// page, which already is a spread, is shown alone and does not take its neighbour with it.
    /// </summary>
    public static List<int[]> Spreads(int count, Func<int, bool> isWide, bool coverAlone)
    {
        var groups = new List<int[]>();
        var i = 0;
        while (i < count)
        {
            var alone = (i == 0 && coverAlone) || isWide(i) || i + 1 >= count || isWide(i + 1);
            if (alone)
            {
                groups.Add([i]);
                i++;
            }
            else
            {
                groups.Add([i, i + 1]);
                i += 2;
            }
        }
        return groups;
    }

    /// <summary>Index of the group that shows <paramref name="page"/>.</summary>
    public static int GroupOf(List<int[]> groups, int page)
    {
        for (var g = 0; g < groups.Count; g++)
        {
            if (groups[g].Contains(page))
            {
                return g;
            }
        }
        return Math.Max(0, groups.Count - 1);
    }
}
