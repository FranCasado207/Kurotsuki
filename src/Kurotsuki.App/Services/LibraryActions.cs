using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Kurotsuki.Core.I18n;
using Kurotsuki.Core.Library;

namespace Kurotsuki.App.Services;

/// <summary>Menu actions about the whole library: statistics and a CSV export of the favourites.</summary>
public static class LibraryActions
{
    public static string StatsText(AppServices services)
    {
        var posts = services.Favorites.LoadPosts();
        var stats = LibraryReport.Stats(posts);
        var lines = new List<string>
        {
            Messages.Get("next.statsFavorites", stats.Posts),
            Messages.Get("next.statsFolders", services.Folders.Names.Count),
            Messages.Get("next.statsDuplicates", services.Queue.Duplicates?.Count ?? 0),
            Messages.Get("next.statsCache", Math.Round(services.Cache.SizeBytes() / 1024.0 / 1024, 1)),
        };
        if (stats.Posts > 0)
        {
            lines.Add("");
            lines.Add(Messages.Get("next.statsBoorus") + ": " + string.Join(", ", stats.Boorus.OrderByDescending(b => b.Value).Select(b => $"{b.Key} ({b.Value})")));
            lines.Add(Messages.Get("next.statsRatings") + ": " + string.Join(", ", stats.Ratings.OrderByDescending(r => r.Value).Select(r => $"{r.Key} ({r.Value})")));
            lines.Add("");
            lines.Add(Messages.Get("next.statsTopTags"));
            lines.AddRange(stats.TopTags.Select(t => $"{t.Count,5}  {t.Tag.Replace('_', ' ')}"));
        }
        return string.Join("\n", lines);
    }

    public static Task ShowStatsAsync(Window? owner, AppServices services) =>
        Dialogs.ShowTextAsync(owner, Messages.Get("next.statsTitle"), StatsText(services));

    public static async Task ExportCsvAsync(Window owner, AppServices services)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Messages.Get("next.exportFavorites"),
            SuggestedFileName = $"kurotsuki-favorites-{DateTime.Now:yyyyMMdd}.csv",
            DefaultExtension = "csv",
            FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }],
        });
        if (file?.TryGetLocalPath() is not { } path)
        {
            return;
        }
        try
        {
            var posts = services.Favorites.LoadPosts();
            await File.WriteAllTextAsync(path, LibraryReport.ToCsv(posts), new System.Text.UTF8Encoding(true));
            await Dialogs.MessageAsync(owner, Messages.Get("next.exportFavorites"), Messages.Get("next.favoritesExported", posts.Count, path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await Dialogs.MessageAsync(owner, Messages.Get("next.exportFavorites"), Messages.Get("backup.error", e.Message));
        }
    }
}
