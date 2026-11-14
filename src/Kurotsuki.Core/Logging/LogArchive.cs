using System.Formats.Tar;
using System.Reflection;
using System.Text;
using Kurotsuki.Core.Config;

namespace Kurotsuki.Core.Logging;

/// <summary>Bundles the log files and a system summary into one tar, ready to attach to a bug report.</summary>
public static class LogArchive
{
    public const string FileName = "kurotsuki-logs.tar";

    /// <summary>Writes <paramref name="target"/> (replacing it) with every <c>*.log</c> in <paramref name="logDir"/> plus system-info.txt.</summary>
    public static string Write(string logDir, string target, string systemInfo)
    {
        var logs = Directory.Exists(logDir) ? Directory.GetFiles(logDir, "*.log").Order().ToList() : [];
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        var partial = target + ".part";
        try
        {
            using (var stream = File.Create(partial))
            using (var tar = new TarWriter(stream, TarEntryFormat.Ustar))
            {
                tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "system-info.txt")
                { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(systemInfo)) });
                foreach (var log in logs)
                {
                    using var data = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "logs/" + Path.GetFileName(log)) { DataStream = data });
                }
            }
            File.Move(partial, target, overwrite: true);
        }
        finally
        {
            File.Delete(partial);
        }
        return target;
    }
}

/// <summary>A text summary of the environment for the log archive; contains no keys, passwords or user names.</summary>
public static class SystemInfo
{
    public static string Version() =>
        typeof(SystemInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";

    public static string Describe(IReadOnlyList<BooruConfiguration>? boorus)
    {
        var text = new StringBuilder();
        text.Append("Kurotsuki-Next ").Append(Version()).Append('\n');
        text.Append(".NET ").Append(Environment.Version).Append(" (").Append(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription).Append(")\n");
        text.Append("OS ").Append(System.Runtime.InteropServices.RuntimeInformation.OSDescription).Append(' ')
            .Append(System.Runtime.InteropServices.RuntimeInformation.OSArchitecture).Append('\n');
        text.Append("Culture ").Append(System.Globalization.CultureInfo.CurrentCulture.Name).Append('\n');
        text.Append("Processors ").Append(Environment.ProcessorCount).Append('\n');
        text.Append("Memory working set MB ").Append(Environment.WorkingSet >> 20).Append('\n');
        text.Append("Boorus ").Append(boorus?.Count ?? 0).Append('\n');
        foreach (var booru in boorus ?? [])
        {
            var host = Uri.TryCreate(booru.BaseUrl?.Trim(), UriKind.Absolute, out var uri) ? uri.Host : "?";
            text.Append("  ").Append(booru.EffectiveEngine).Append(' ').Append(host).Append('\n');
        }
        return text.ToString();
    }
}
