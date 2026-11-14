using Avalonia.Media;
using Kurotsuki.Core.Booru;

namespace Kurotsuki.App.Services;

/// <summary>Tag category colours, picked for contrast on the active theme's surface.</summary>
public static class TagColors
{
    // Light themes need darker tones to stay readable on a pale surface.
    public static IBrush BrushOf(TagCategory category) => new SolidColorBrush(Color.Parse(ThemeManager.IsDark
        ? category switch
        {
            TagCategory.Artist => "#E27A7C",
            TagCategory.Copyright => "#D77FD8",
            TagCategory.Character => "#34C759",
            TagCategory.General => "#5AA2FA",
            TagCategory.Meta => "#F2994A",
            _ => "#9AA5B1",
        }
        : category switch
        {
            TagCategory.Artist => "#B42318",
            TagCategory.Copyright => "#A21CAF",
            TagCategory.Character => "#15803D",
            TagCategory.General => "#1D4ED8",
            TagCategory.Meta => "#C2410C",
            _ => "#5B6370",
        }));

    public static int Order(TagCategory category) => category switch
    {
        TagCategory.Artist => 0,
        TagCategory.Copyright => 1,
        TagCategory.Character => 2,
        TagCategory.General => 3,
        TagCategory.Meta => 4,
        _ => 5,
    };

    public static string LabelKey(TagCategory category) => $"tag.category.{category.ToString().ToLowerInvariant()}";
}
