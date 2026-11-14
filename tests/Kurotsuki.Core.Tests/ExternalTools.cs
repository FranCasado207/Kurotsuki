using System.Diagnostics;

namespace Kurotsuki.Core.Tests;

/// <summary>
/// Runs real tools (7z, unrar, ffprobe, zbarimg) to check our output with an independent implementation.
/// When a tool is not installed the check is skipped (the test then only checks what it can without it).
/// </summary>
internal static class ExternalTools
{
    public static string? Find(string name)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    public static (int ExitCode, string Output) Run(string tool, params string[] args)
    {
        var info = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args)
        {
            info.ArgumentList.Add(a);
        }
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}
