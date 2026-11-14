using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Kurotsuki.App.Services;

public enum TrayState { Idle, Downloading, Done }

/// <summary>Draws the tray icon: a dark disc, a progress ring and a download arrow (a check once everything is saved).</summary>
public static class TrayIconRenderer
{
    public const int Size = 64;

    public readonly record struct Palette(Color Disc, Color Track, Color Ring, Color Glyph, Color Done);

    /// <summary>Colours of the active theme, so the icon follows Dark, Light and custom themes.</summary>
    public static Palette ThemePalette()
    {
        static Color Of(string key, Color fallback) =>
            Application.Current?.FindResource(key) is SolidColorBrush brush ? brush.Color : fallback;
        return new Palette(
            Of("KtSurfaceRaisedBrush", Color.Parse("#1D2026")),
            Of("KtBorderBrush", Color.Parse("#2A2E36")),
            Of("KtAccentBrush", Color.Parse("#5EEAD4")),
            Of("KtTextBrush", Color.Parse("#E8EAEE")),
            Of("KtSuccessBrush", Color.Parse("#4ADE80")));
    }

    public static RenderTargetBitmap Render(double progress, TrayState state, Palette palette)
    {
        var visual = new IconVisual(Math.Clamp(progress, 0, 1), state, palette) { Width = Size, Height = Size };
        visual.Measure(new Size(Size, Size));
        visual.Arrange(new Rect(0, 0, Size, Size));
        var bitmap = new RenderTargetBitmap(new PixelSize(Size, Size), new Vector(96, 96));
        bitmap.Render(visual);
        return bitmap;
    }

    private sealed class IconVisual(double progress, TrayState state, Palette palette) : Control
    {
        public override void Render(DrawingContext context)
        {
            const double radius = 25;
            var center = new Point(Size / 2.0, Size / 2.0);
            var ring = state == TrayState.Done ? palette.Done : palette.Ring;
            context.DrawEllipse(new SolidColorBrush(palette.Disc), null, center, Size / 2.0 - 1, Size / 2.0 - 1);
            var track = new Pen(new SolidColorBrush(palette.Track), 6);
            context.DrawEllipse(null, track, center, radius, radius);

            var fill = state == TrayState.Done ? 1 : progress;
            var stroke = new Pen(new SolidColorBrush(ring), 6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            if (fill >= 0.999)
            {
                context.DrawEllipse(null, stroke, center, radius, radius);
            }
            else if (fill > 0.01)
            {
                var angle = fill * 2 * Math.PI;
                var start = new Point(center.X, center.Y - radius);
                var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
                var arc = new StreamGeometry();
                using (var g = arc.Open())
                {
                    g.BeginFigure(start, false);
                    g.ArcTo(end, new Size(radius, radius), 0, fill > 0.5, SweepDirection.Clockwise);
                    g.EndFigure(false);
                }
                context.DrawGeometry(null, stroke, arc);
            }

            var glyph = new Pen(new SolidColorBrush(state == TrayState.Done ? palette.Done : palette.Glyph), 5,
                lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            if (state == TrayState.Done)
            {
                context.DrawLine(glyph, new Point(23, 33), new Point(29.5, 39.5));
                context.DrawLine(glyph, new Point(29.5, 39.5), new Point(42, 25.5));
            }
            else
            {
                context.DrawLine(glyph, new Point(32, 21), new Point(32, 41));
                context.DrawLine(glyph, new Point(23.5, 33), new Point(32, 41.5));
                context.DrawLine(glyph, new Point(40.5, 33), new Point(32, 41.5));
            }
        }
    }
}
