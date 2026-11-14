using Kurotsuki.Core.Session;
using Avalonia.Threading;
using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Config;
using Kurotsuki.Core.Download;
using Kurotsuki.Core.I18n;
using Kurotsuki.Core.Logging;
using Kurotsuki.Core.Media;
using Kurotsuki.Core.Library;
using Kurotsuki.Core.Net;
using Kurotsuki.Core.Storage;
using Kurotsuki.Core.Theming;

namespace Kurotsuki.App.Services;

/// <summary>Composition root: everything the view models share. Created once, on the UI thread.</summary>
public sealed class AppServices : IDisposable
{
    public required AppPaths Paths { get; init; }
    public required ConfigManager ConfigManager { get; init; }
    public required UserConfiguration Config { get; init; }
    public required MediaDownloader Downloader { get; init; }
    public required ImageCache Cache { get; init; }
    public required BooruClientFactory Clients { get; init; }
    public required DownloadQueue Queue { get; init; }
    public required LibraryStore Store { get; init; }
    public required Favorites Favorites { get; init; }
    public required TagGroups TagGroups { get; init; }
    public required PostFolders Folders { get; init; }
    public required PoolReading Reading { get; init; }
    public required PoolArchiver PoolArchiver { get; init; }
    public required WindowLayout Layout { get; init; }
    public required SearchHistory History { get; init; }
    public required BlacklistExceptions BlacklistAllowed { get; init; }
    public required ThemeManager Theme { get; init; }
    public required SessionStore Session { get; init; }
    public required NextSettings Prefs { get; init; }
    public required IFrameDecoder FrameDecoder { get; init; }

    /// <summary>Problems found while starting (corrupt config, unreadable database); shown in the status bar.</summary>
    public List<string> StartupMessages { get; } = [];

    /// <summary>Set by the main view model; where errors from background work end up.</summary>
    public Action<string> ReportError { get; set; } = _ => { };

    /// <summary>Set by the main view model.</summary>
    public INavigator Navigator { get; set; } = null!;

    /// <param name="apiClient">HTTP client for booru APIs; tests pass one with a stubbed handler.</param>
    /// <param name="mediaClient">HTTP client for thumbnails and files.</param>
    /// <param name="stateDirectory">Where the session and next-settings files go; tests give each test its own so they cannot affect each other.</param>
    public static AppServices Create(HttpClient? apiClient = null, HttpClient? mediaClient = null, string? stateDirectory = null)
    {
        var paths = AppPaths.Get();
        var messages = new List<string>();
        AppLog.Install(Path.Combine(paths.Data, "logs"));
        AppLog.Info($"Kurotsuki-Next {SystemInfo.Version()} starting");
        var manager = new ConfigManager(paths);
        var config = LoadConfig(manager, messages);
        Messages.SetLanguage(config.Language);

        LibraryStore store;
        try
        {
            store = new LibraryStore(paths.DatabaseFile);
        }
        catch (Exception e)
        {
            // Keep the app usable: favourites, history and the queue then last only for this session.
            messages.Add($"The library database could not be opened ({e.Message}); changes will not be saved.");
            store = new LibraryStore(":memory:");
        }
        ImportLegacyLibrary(config, store, manager, messages);

        AppServices? self = null;
        void Report(string error) => self?.ReportError(error);

        var auth = new RequestAuth(() => config.BooruConfigurations);
        var downloader = new MediaDownloader(auth, client: mediaClient);
        var cache = new ImageCache(() => config.CacheFolder, paths.Cache, downloader);
        var decoder = new SkiaFrameDecoder();
        var clients = new BooruClientFactory(apiClient ?? BooruClientFactory.CreateHttpClient());

        var favorites = new Favorites(store, Report);
        var groups = new TagGroups(store, favorites, Report);
        var archiver = new PoolArchiver(
            () => config.PoolArchiveFormat,
            pool => SourceFor(config, clients, pool),
            (url, ct) => cache.FetchAsync(url, ct));
        var queue = new DownloadQueue(
            downloader, archiver,
            () => new DownloadSettings(config.DownloadFolder, config.DownloadTemplate,
                SidecarFormats.Parse(config.DownloadSidecar), SkipKnown: !config.RedownloadKnown),
            groups.GroupFor, store, Report, action => Dispatcher.UIThread.Post(action));

        self = new AppServices
        {
            Paths = paths,
            ConfigManager = manager,
            Config = config,
            Downloader = downloader,
            Cache = cache,
            Clients = clients,
            Queue = queue,
            Store = store,
            Favorites = favorites,
            TagGroups = groups,
            Folders = new PostFolders(store, Report),
            Reading = new PoolReading(store, Report),
            PoolArchiver = archiver,
            Layout = new WindowLayout(config),
            History = new SearchHistory(Path.Combine(paths.Data, "search-history.json")),
            BlacklistAllowed = new BlacklistExceptions(Path.Combine(paths.Data, "blacklist-allowed.json")),
            Theme = new ThemeManager(new ThemeStore(paths.Config)),
            Session = new SessionStore(stateDirectory ?? paths.Config),
            Prefs = new NextSettings(stateDirectory ?? paths.Config),
            FrameDecoder = decoder,
        };
        self.Theme.Apply(self.Theme.Saved());
        ThemeManager.ApplyTextSize(self.Prefs.TextSize);
        queue.PostProcess = (path, _) => Task.Run(() => ConvertUgoira(path, decoder));
        queue.Duplicates = new DuplicateIndex(Path.Combine(paths.Data, "next-downloaded-hashes.txt"));
        // Keep the thumbnail cache within its limit, off the UI thread (the Java app trims at start too).
        _ = Task.Run(() =>
        {
            try { cache.Trim(); } catch (Exception e) { AppLog.Warning("Could not trim the cache", e); }
        });
        self.StartupMessages.AddRange(messages);
        return self;
    }

    /// <summary>A downloaded ugoira zip becomes an animated GIF next to it (the zip is removed); anything else is left alone.</summary>
    private static string ConvertUgoira(string path, IFrameDecoder decoder)
    {
        if (!Ugoira.IsUgoiraZip(path))
        {
            return path;
        }
        var gif = FileNameTemplate.UniqueTarget(Path.GetDirectoryName(path)!, Path.GetFileName(Ugoira.GifNameFor(path)));
        Ugoira.ConvertToGif(path, gif, decoder);
        File.Delete(path);
        AppLog.Info("Converted an ugoira to a GIF");
        return gif;
    }

    private static UserConfiguration LoadConfig(ConfigManager manager, List<string> messages)
    {
        try
        {
            return manager.Load();
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or IOException)
        {
            // Never overwrite a file we could not read: it may hold the user's API keys. Keep it aside.
            var aside = manager.FilePath + $".unreadable-{DateTime.Now:yyyyMMdd-HHmmss}";
            try
            {
                File.Move(manager.FilePath, aside);
                messages.Add($"config.json could not be read ({e.Message}); it was kept as {Path.GetFileName(aside)}.");
            }
            catch (IOException)
            {
                messages.Add($"config.json could not be read: {e.Message}");
            }
            return new UserConfiguration();
        }
    }

    /// <summary>Favourites and folders used to live in config.json; the Java app moved them into the database once. Same here.</summary>
    private static void ImportLegacyLibrary(UserConfiguration config, LibraryStore store, ConfigManager manager, List<string> messages)
    {
        try
        {
            var imported = store.MigrateFromConfig(config.FavoriteTags, config.FavoritePosts,
                config.PostFolders.Where(f => f.Name is not null).Select(f => (f.Name!, (IEnumerable<Post>)(f.Posts ?? []))));
            if (!imported)
            {
                return;
            }
            config.FavoriteTags = [];
            config.FavoritePosts = [];
            config.PostFolders = [];
            manager.Save(config);
        }
        catch (Exception e)
        {
            messages.Add($"Could not import favourites from config.json: {e.Message}");
        }
    }

    /// <summary>The configured booru whose base URL a stored post or pool carries; null when it was removed from the settings.</summary>
    public BooruConfiguration? FindBooru(string? baseUrl)
    {
        if (baseUrl is null)
        {
            return null;
        }
        var wanted = baseUrl.Trim().TrimEnd('/');
        return Config.BooruConfigurations.FirstOrDefault(b => b.BaseUrl?.Trim().TrimEnd('/') == wanted);
    }

    private static PoolArchiver.Source? SourceFor(UserConfiguration config, BooruClientFactory clients, Pool pool)
    {
        var wanted = pool.BooruBaseUrl?.Trim().TrimEnd('/');
        foreach (var booru in config.BooruConfigurations)
        {
            if (booru.BaseUrl?.Trim().TrimEnd('/') != wanted)
            {
                continue;
            }
            var client = clients.ClientFor(booru);
            if (client.SupportsPools)
            {
                var host = Uri.TryCreate(booru.BaseUrl, UriKind.Absolute, out var uri) ? uri.Host : booru.KindLabel;
                return new PoolArchiver.Source(client, booru.BaseUrl!, host);
            }
        }
        return null;
    }

    public void SaveConfig()
    {
        try
        {
            ConfigManager.Save(Config);
        }
        catch (Exception e)
        {
            ReportError(Messages.Get("common.saveError", e.Message));
        }
    }

    public void Dispose() => Store.Dispose();
}
