using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Kurotsuki.Core.Theming;

namespace Kurotsuki.App.Services;

/// <summary>
/// Applies a <see cref="ThemeDefinition"/>: recolours the shared brushes in place (so every DynamicResource and StaticResource user
/// follows) and feeds the same colours to Fluent's palette so stock controls match.
/// </summary>
public sealed class ThemeManager(ThemeStore store)
{
    public static bool IsDark { get; private set; } = true;

    public ThemeStore Store { get; } = store;
    public ThemeDefinition Current { get; private set; } = ThemeDefinition.Dark;

    /// <summary>Sets the base text size for every window and for the stock controls (which read their own size resource).</summary>
    public static void ApplyTextSize(double size)
    {
        if (Application.Current is { } app)
        {
            app.Resources["KtBaseFontSize"] = size;
            app.Resources["ControlContentThemeFontSize"] = size;
        }
    }

    /// <summary>The theme saved by the user, or Dark; the system's Dark/Light when "follow system" is on.</summary>
    public ThemeDefinition Saved() => Store.FollowsSystem() ? ForSystem() : Store.Find(Store.SelectedName());

    /// <summary>The built-in Dark or Light theme matching the operating system's current setting (Dark when it cannot be told).</summary>
    public ThemeDefinition ForSystem() => SystemIsDark() == false ? ThemeDefinition.Light : ThemeDefinition.Dark;

    public static bool? SystemIsDark() =>
        Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant switch
        {
            Avalonia.Platform.PlatformThemeVariant.Dark => true,
            Avalonia.Platform.PlatformThemeVariant.Light => false,
            _ => null,
        };

    /// <summary>Re-applies the saved theme when the operating system switches between dark and light.</summary>
    public void WatchSystem()
    {
        if (Application.Current?.PlatformSettings is { } settings)
        {
            settings.ColorValuesChanged += (_, _) =>
            {
                if (Store.FollowsSystem())
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => Apply(ForSystem()));
                }
            };
        }
    }

    public void Apply(ThemeDefinition theme)
    {
        Current = theme;
        IsDark = theme.IsDark;
        if (Application.Current is not { } app)
        {
            return;
        }
        var c = (string token) => Color.Parse(theme.Color(token));
        var background = c("background");
        var surface = c("surface");
        var raised = c("raised");
        var border = c("border");
        var text = c("text");
        var muted = c("textMuted");
        var accent = c("accent");

        Set(app, "KtBackgroundBrush", background);
        Set(app, "KtSurfaceBrush", surface);
        Set(app, "KtSurfaceRaisedBrush", raised);
        Set(app, "KtBorderBrush", border);
        Set(app, "KtTextBrush", text);
        Set(app, "KtTextMutedBrush", muted);
        Set(app, "KtAccentBrush", accent);
        Set(app, "KtOnAccentBrush", c("onAccent"));
        Set(app, "KtDangerBrush", c("danger"));
        Set(app, "KtSuccessBrush", c("success"));
        Set(app, "KtHoverBrush", Mix(raised, text, 0.08));
        // Accent buttons shift colour on hover instead of fading, so the label never loses contrast.
        Set(app, "KtAccentHoverBrush", Mix(accent, text, 0.16));
        Set(app, "KtAccentPressedBrush", Mix(accent, text, 0.3));

        app.RequestedThemeVariant = theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        var variant = theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        foreach (var fluent in app.Styles.OfType<Avalonia.Themes.Fluent.FluentTheme>())
        {
            var palette = new Avalonia.Themes.Fluent.ColorPaletteResources
            {
                Accent = accent,
                RegionColor = background,
                ErrorText = c("danger"),
                AltHigh = background,
                AltMediumHigh = background,
                AltMedium = background,
                AltMediumLow = background,
                AltLow = background,
                BaseHigh = text,
                BaseMediumHigh = Mix(text, background, 0.1),
                BaseMedium = Mix(text, background, 0.35),
                BaseMediumLow = muted,
                BaseLow = Mix(border, background, 0.5),
                ChromeLow = surface,
                ChromeMediumLow = surface,
                ChromeMedium = raised,
                ChromeHigh = border,
                ChromeAltLow = Mix(text, background, 0.15),
                ChromeDisabledLow = Mix(muted, background, 0.5),
                ChromeDisabledHigh = border,
                ListLow = raised,
                ListMedium = border,
            };
            fluent.Palettes[variant] = palette;
        }
    }

    private static void Set(Application app, string key, Color color)
    {
        if (app.TryGetResource(key, null, out var existing) && existing is SolidColorBrush brush)
        {
            brush.Color = color;
        }
        else
        {
            app.Resources[key] = new SolidColorBrush(color);
        }
    }

    internal static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));
}
