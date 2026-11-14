using System.Diagnostics;
using System.Text.Json;
using Kurotsuki.App.Services;
using Kurotsuki.App.ViewModels;
using Kurotsuki.Core.Backup;

namespace Kurotsuki.App.Tests;

public class BackupUiTests
{
    private static string ConfigPath => Path.Combine(Harness.Root, "cfg", "kurotsuki", "config.json");

    [Fact]
    public Task BackupFileRoundTripKeepsSecretsOwnerOnlyAndRefreshesTheLiveLists() => Harness.Run(async () =>
    {
        var original = File.ReadAllText(ConfigPath); // the import saves the config: put the harness config back afterwards
        try
        {
            var services = Harness.NewServices();
            var sample = Path.Combine(AppContext.BaseDirectory, "backup-v1-sample.json");
            var summary = BackupActions.ReadBackup(services, sample);
            Assert.True(summary.FavoriteTags > 0);
            // the live lists already show it (no restart needed)
            Assert.Contains("cat", services.Favorites.Tags);
            Assert.Contains(services.TagGroups.Groups, g => g.Name == "Pets");
            Assert.Contains("Best", services.Folders.Names);

            var withKeys = Path.Combine(Harness.Root, "backup-with-keys.json");
            BackupActions.WriteBackup(services, withKeys, includeSecrets: true);
            using (var doc = JsonDocument.Parse(File.ReadAllText(withKeys)))
            {
                Assert.Equal("kurotsuki-backup", doc.RootElement.GetProperty("format").GetString());
                Assert.True(doc.RootElement.GetProperty("includesSecrets").GetBoolean());
            }
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(withKeys));
            }
            var withoutKeys = Path.Combine(Harness.Root, "backup-no-keys.json");
            BackupActions.WriteBackup(services, withoutKeys, includeSecrets: false);
            Assert.DoesNotContain("SECRET1", File.ReadAllText(withoutKeys));

            // an unreadable file is refused with a clear message
            File.WriteAllText(Path.Combine(Harness.Root, "bad.json"), "{}");
            Assert.Throws<BackupFile.BackupException>(() => BackupActions.ReadBackup(services, Path.Combine(Harness.Root, "bad.json")));
        }
        finally
        {
            File.WriteAllText(ConfigPath, original);
        }
        await Task.CompletedTask;
    });

    [Fact]
    public Task TransferShowsAQrCodeThatAPhoneScannerReadsThenServesTheBackupOnce() => Harness.Run(async () =>
    {
        var vm = new TransferViewModel(Harness.NewServices());
        vm.ShowCommand.Execute(null);
        try
        {
            Assert.True(vm.Running);
            Assert.NotNull(vm.Qr);
            Assert.StartsWith("kurotsuki://transfer?h=", vm.Link);
            var parsed = TransferLink.Parse(vm.Link);
            Assert.NotNull(parsed);
            Assert.Equal(vm.ServerPort, parsed!.Port);

            // what a phone does: read the QR with a real decoder, then fetch the backup from the link
            var zbar = Environment.GetEnvironmentVariable("PATH")!.Split(':').Select(d => Path.Combine(d, "zbarimg")).FirstOrDefault(File.Exists);
            if (zbar is not null)
            {
                var png = Path.Combine(Harness.Root, "qr.png");
                using (var stream = File.Create(png))
                {
#pragma warning disable CS0618 // the replacement overload needs encoder options we do not care about
                    vm.Qr!.Save(stream);
#pragma warning restore CS0618
                }
                var info = new ProcessStartInfo(zbar) { RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var a in new[] { "--quiet", "--raw", png }) info.ArgumentList.Add(a);
                using var p = Process.Start(info)!;
                var scanned = p.StandardOutput.ReadToEnd().TrimEnd('\n');
                p.WaitForExit();
                Assert.Equal(vm.Link, scanned);
            }

            using var http = new HttpClient();
            var body = await http.GetStringAsync($"http://127.0.0.1:{parsed.Port}/backup?t={parsed.Token}");
            Assert.Contains("kurotsuki-backup", body);
            await Harness.WaitFor(() => !vm.Running, 5000);
            Assert.Null(vm.Qr);
            Assert.Contains("Done", vm.Status);
        }
        finally
        {
            vm.Dispose();
        }
    });

    [Fact]
    public Task ExportedLogsContainTheSessionAndSystemInfoButNoSecrets() => Harness.Run(async () =>
    {
        var services = Harness.NewServices();
        Kurotsuki.Core.Logging.AppLog.Info("ui test line");
        var tar = BackupActions.WriteLogs(services);
        Assert.True(File.Exists(tar));
        using var stream = File.OpenRead(tar);
        using var reader = new System.Formats.Tar.TarReader(stream);
        var entries = new Dictionary<string, string>();
        while (reader.GetNextEntry() is { } e)
        {
            entries[e.Name] = new StreamReader(e.DataStream!).ReadToEnd();
        }
        Assert.Contains("system-info.txt", entries.Keys);
        Assert.Contains(entries, kv => kv.Key.StartsWith("logs/") && kv.Value.Contains("starting"));
        Assert.DoesNotContain(entries.Values, v => v.Contains("apiKey"));
        await Task.CompletedTask;
    });
}
