using System.Text;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Kurotsuki.Core.Backup;
using Kurotsuki.Core.Config;
using Kurotsuki.Core.I18n;
using Kurotsuki.Core.Logging;

namespace Kurotsuki.App.Services;

/// <summary>Export/import backup and log export: the file work (testable) and the dialogs around it.</summary>
public static class BackupActions
{
    // ---- file work ------------------------------------------------------------------------------------------

    /// <summary>Writes the backup. With secrets the file is created owner-only from the start, like config.json.</summary>
    public static void WriteBackup(AppServices services, string path, bool includeSecrets)
    {
        var json = BackupFile.Export(services.Config, services.Store, includeSecrets);
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (includeSecrets && !OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }
        using var stream = new FileStream(path, options);
        stream.Write(new UTF8Encoding(false).GetBytes(json));
    }

    /// <summary>Merges a backup file into the library and configuration and refreshes the live lists.</summary>
    public static BackupFile.Summary ReadBackup(AppServices services, string path)
    {
        if (new FileInfo(path).Length > BackupFile.MaxBytes)
        {
            throw new BackupFile.BackupException(Messages.Get("backup.tooLarge"));
        }
        var summary = BackupFile.Import(File.ReadAllText(path, Encoding.UTF8), services.Config, services.Store);
        services.SaveConfig();
        services.Favorites.Reload();
        services.TagGroups.Reload();
        services.Folders.Reload();
        services.Reading.Reload();
        AppLog.Info($"Backup imported: boorus={summary.Boorus} tags={summary.FavoriteTags} posts={summary.FavoritePosts + summary.FolderPosts} skipped={summary.Skipped}");
        return summary;
    }

    public static string WriteLogs(AppServices services)
    {
        var target = Path.Combine(services.Paths.Data, LogArchive.FileName);
        return LogArchive.Write(Path.Combine(services.Paths.Data, "logs"), target, SystemInfo.Describe(services.Config.BooruConfigurations));
    }

    // ---- dialogs --------------------------------------------------------------------------------------------

    private static FilePickerFileType JsonType() => new(Messages.Get("next.backupFilter")) { Patterns = ["*.json"] };

    public static async Task ExportAsync(Window owner, AppServices services)
    {
        var secrets = await Dialogs.ConfirmAsync(owner, Messages.Get("backup.exportTitle"),
            Messages.Get("backup.exportAsk") + "\n\n" + Messages.Get("backup.includeKeys") + "?");
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Messages.Get("next.saveBackup"),
            SuggestedFileName = $"kurotsuki-backup-{DateTime.Now:yyyyMMdd}.json",
            DefaultExtension = "json",
            FileTypeChoices = [JsonType()],
        });
        if (file?.TryGetLocalPath() is not { } path)
        {
            return;
        }
        try
        {
            WriteBackup(services, path, secrets);
            await Dialogs.MessageAsync(owner, Messages.Get("backup.exportTitle"), Messages.Get("backup.exported", path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await Dialogs.MessageAsync(owner, Messages.Get("backup.exportTitle"), Messages.Get("backup.error", e.Message));
        }
    }

    /// <summary>Returns true when something was imported (the caller reloads its boorus).</summary>
    public static async Task<bool> ImportAsync(Window owner, AppServices services)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Messages.Get("next.openBackup"),
            AllowMultiple = false,
            FileTypeFilter = [JsonType()],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path)
        {
            return false;
        }
        try
        {
            var s = ReadBackup(services, path);
            await Dialogs.MessageAsync(owner, Messages.Get("backup.importTitle"), Messages.Get("backup.imported",
                s.Boorus, s.FavoriteTags, s.FavoritePosts + s.FolderPosts, s.Reading, s.Skipped));
            return true;
        }
        catch (Exception e) when (e is BackupFile.BackupException or IOException or UnauthorizedAccessException)
        {
            await Dialogs.MessageAsync(owner, Messages.Get("backup.importTitle"), Messages.Get("backup.error", e.Message));
            return false;
        }
    }

    public static async Task ExportLogsAsync(Window owner, AppServices services)
    {
        try
        {
            var path = WriteLogs(services);
            await Dialogs.MessageAsync(owner, Messages.Get("menu.exportLogs"), Messages.Get("menu.logsExported", path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await Dialogs.MessageAsync(owner, Messages.Get("menu.exportLogs"), Messages.Get("backup.error", e.Message));
        }
    }
}
