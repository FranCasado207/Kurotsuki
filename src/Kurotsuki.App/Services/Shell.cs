using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace Kurotsuki.App.Services;

/// <summary>Things view models need from the desktop: the clipboard, the browser, the file manager, the main window.</summary>
public static class Shell
{
    /// <summary>Types we are willing to hand to the desktop's default handler. Anything else (scripts, .desktop…) is refused.</summary>
    private static readonly HashSet<string> OpenableExtensions =
    [
        "jpg", "jpeg", "png", "gif", "webp", "bmp", "mp4", "webm", "mkv", "mov", "avi", "swf", "zip", "cbz", "cbr", "cbt",
        "rar", "tar", "7z", "cb7", "txt", "json",
    ];

    public static Window? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public static async Task CopyToClipboardAsync(string text)
    {
        if (MainWindow?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    /// <summary>Opens http(s) URLs only; a URL from a remote server must never launch anything else.</summary>
    public static bool OpenUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }
        return Launch(uri.AbsoluteUri);
    }

    public static bool OpenFolder(string? folder) => folder is not null && Directory.Exists(folder) && Launch(folder);

    /// <summary>Opens a local media file with the default application, but only types in the allow-list.</summary>
    public static bool OpenFile(string? file)
    {
        if (file is null || !File.Exists(file))
        {
            return false;
        }
        var ext = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
        return OpenableExtensions.Contains(ext) && Launch(file);
    }

    public static bool IsOpenable(string extension) => OpenableExtensions.Contains(extension.ToLowerInvariant());

    private static bool Launch(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
