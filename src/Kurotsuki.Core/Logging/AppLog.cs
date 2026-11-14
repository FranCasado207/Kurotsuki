using System.Globalization;
using System.Text;

namespace Kurotsuki.Core.Logging;

public enum LogLevel { Info, Warning, Error }

/// <summary>
/// Writes the application's log to a few rotating files in the data folder (<c>logs/kurotsuki-N.log</c>, 1 MB × 5), one line per
/// event. Lines carry hosts, counts, sizes and timings, never full URLs, tags, keys or file contents (use <see cref="SafeUrl"/>).
/// </summary>
public static class AppLog
{
    internal const int FileBytes = 1_000_000;
    internal const int FileCount = 5;
    private static readonly object Gate = new();
    private static string? _directory;

    public static string? Directory => _directory;

    /// <summary>Starts file logging into <paramref name="directory"/>; safe to call twice.</summary>
    public static void Install(string directory)
    {
        lock (Gate)
        {
            if (_directory is not null)
            {
                return;
            }
            try
            {
                System.IO.Directory.CreateDirectory(directory);
                _directory = directory;
            }
            catch (IOException e)
            {
                Console.Error.WriteLine("Could not start file logging: " + e.Message);
            }
        }
    }

    /// <summary>For tests: forget the current directory.</summary>
    internal static void Reset()
    {
        lock (Gate)
        {
            _directory = null;
        }
    }

    public static void Info(string message) => Write(LogLevel.Info, message, null);
    public static void Warning(string message, Exception? error = null) => Write(LogLevel.Warning, message, error);
    public static void Error(string message, Exception? error = null) => Write(LogLevel.Error, message, error);

    private static void Write(LogLevel level, string message, Exception? error)
    {
        var line = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append(' ')
            .Append(level.ToString().ToUpperInvariant().PadRight(7)).Append(' ')
            .Append(message.ReplaceLineEndings(" "));
        if (error is not null)
        {
            line.Append(" | ").Append(error.GetType().Name).Append(": ").Append(error.Message.ReplaceLineEndings(" "));
            foreach (var frame in (error.StackTrace ?? "").Split('\n').Take(12))
            {
                line.Append("\n    ").Append(frame.Trim());
            }
        }
        line.Append(Environment.NewLine);

        lock (Gate)
        {
            if (_directory is null)
            {
                return;
            }
            try
            {
                var current = Path.Combine(_directory, "kurotsuki-0.log");
                if (File.Exists(current) && new FileInfo(current).Length >= FileBytes)
                {
                    Rotate(_directory);
                }
                File.AppendAllText(current, line.ToString(), new UTF8Encoding(false));
            }
            catch (IOException)
            {
                // logging must never break the app
            }
        }
    }

    private static void Rotate(string directory)
    {
        File.Delete(Path.Combine(directory, $"kurotsuki-{FileCount - 1}.log"));
        for (var i = FileCount - 2; i >= 0; i--)
        {
            var from = Path.Combine(directory, $"kurotsuki-{i}.log");
            if (File.Exists(from))
            {
                File.Move(from, Path.Combine(directory, $"kurotsuki-{i + 1}.log"), overwrite: true);
            }
        }
    }

    /// <summary>Host and path of a URL without the query string, which may hold keys or tags.</summary>
    public static string SafeUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host + uri.AbsolutePath : url is null ? "" : "?";
}
