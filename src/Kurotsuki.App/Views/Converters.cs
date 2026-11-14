using Avalonia.Data.Converters;
using Kurotsuki.Core.Download;

namespace Kurotsuki.App.Views;

public static class Converters
{
    public static readonly IValueConverter IsFailed =
        new FuncValueConverter<DownloadStatus, bool>(status => status == DownloadStatus.Failed);

    public static readonly IValueConverter PlayPauseGlyph = new FuncValueConverter<bool, string>(playing => playing ? "⏸" : "▶");
    public static readonly IValueConverter MuteGlyph = new FuncValueConverter<bool, string>(muted => muted ? "🔇" : "🔊");
}
