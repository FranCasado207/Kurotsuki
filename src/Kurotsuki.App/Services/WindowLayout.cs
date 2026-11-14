using Kurotsuki.Core.Config;

namespace Kurotsuki.App.Services;

/// <summary>
/// Remembers window and panel sizes in <c>config.json</c>'s <c>panelSizes</c> map under <c>next.*</c> keys. The Java app
/// loads that whole map and writes it back, so these keys survive it; its own keys (LEFT, BOTTOM…) are not touched.
/// </summary>
public sealed class WindowLayout(UserConfiguration config)
{
    public double Get(string key, double fallback) =>
        config.PanelSizes.TryGetValue("next." + key, out var v) && double.IsFinite(v) && v > 0 ? v : fallback;

    public bool Flag(string key, bool fallback) =>
        config.PanelSizes.TryGetValue("next." + key, out var v) ? v > 0 : fallback;

    public void Set(string key, double value) => config.PanelSizes["next." + key] = value;
    public void SetFlag(string key, bool value) => config.PanelSizes["next." + key] = value ? 1 : 0;

    /// <summary>Drops every remembered size, e.g. after a bad value left a window off-screen.</summary>
    public void Reset()
    {
        foreach (var key in config.PanelSizes.Keys.Where(k => k.StartsWith("next.")).ToList())
        {
            config.PanelSizes.Remove(key);
        }
    }
}
