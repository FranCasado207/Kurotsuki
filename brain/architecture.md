# Architecture

```
Kurotsuki.App (Avalonia)  ──references──▶  Kurotsuki.Core (plain .NET)
  Views (AXAML)                               Config/    AppPaths, ConfigManager, UserConfiguration, BooruConfiguration(+Converter), TagBlacklist
  ViewModels (MVVM Toolkit)                   Booru/     Post, enums, IBooruClient, 7 clients (Clients.cs), BooruClientFactory, BooruHttp, ApiRequest
  Services/AppServices (composition root)     Net/       MediaDownloader, HostThrottle, RequestAuth
  Services/Shell (clipboard, open URL)        Download/  DownloadQueue, DownloadItem, FileNameTemplate, Sidecar, DownloadSettings, ImageCache
                                              Storage/   LibraryStore (SQLite: downloaded + download_queue)
```

## What lives where (session 2)
- Core: `Config/` · `I18n/Messages` (Java bundles) · `Booru/` (`IBooruClient` incl. pools & tag categories, `Clients.cs` + `PoolClients.cs` as partial classes,
  `Pool`, `BooruHttp` URL helpers) · `Net/` (`IMediaDownloader`) · `Download/` (`DownloadQueue` with post+pool items, `PoolArchiver`, `Comic/` zip/tar/RAR writers + ComicInfo,
  `BulkFetch`, `ImageCache` with trim/clear, `ImageSniffer`) · `Library/` (`Favorites`, `TagGroups`, `PostFolders`, `PoolReading`, `TagCategoryResolver`, `SpreadLayout`, `PageBudget`) ·
  `Storage/LibraryStore` (full Java schema).
- App: `ViewModels/` (`MainViewModel` is the `INavigator`; tabs derive from `TabViewModel`; grids from `PostGridViewModel`; `ViewerViewModel`, `ReaderViewModel`, `SettingsViewModel`, `BulkDownloadViewModel`),
  `Views/` (AXAML + code-behind for scrolling/menus/reader layout), `Services/` (`AppServices`, `Menus`, `Dialogs`, `Shell`, `WindowLayout`, `TExtension` for `{svc:T}`).
- Tabs are chosen by `DataTemplate DataType=` in `MainWindow.axaml`. Viewer/reader are separate non-modal windows opened by the navigator (`MainViewModel.Viewer`, `LastReader` expose them for tests).

## Composition
`App.OnFrameworkInitializationCompleted` → `AppServices.Create()` (loads config, opens the DB, builds downloader/cache/queue/factory)
→ `MainWindow { DataContext = MainViewModel }`. `AppServices` is passed by constructor to view models; there is no DI container
(not needed yet — add `Microsoft.Extensions.DependencyInjection` only if the graph grows; it is in the local NuGet cache).

## Threading
- UI thread: all `ObservableCollection`s (`DownloadQueue.Items`, `SearchTabViewModel.Posts`), `DownloadItem` property changes.
- `DownloadQueue` is **constructed on the UI thread** and takes `post: Action<Action>` (Avalonia: `Dispatcher.UIThread.Post`). Transfers run via
  `Task.Run`, throttled by a 2-slot semaphore, and marshal every state change back through `post`.
- `LibraryStore` uses one connection guarded by a lock (transactions via `InTransaction`, nestable); calls are short and made from the UI thread (the Java app used a writer thread —
  if the DB ever gets slow, move writes to a channel). Library services apply the change in memory first, then write, and undo + report if the write fails (`StoreWrites.Persist`).
- A view model that starts work in its constructor may finish **before** its window subscribes to its events (a fast/cached response completes synchronously). Windows must render the current state at construction (see `ReaderWindow.RebuildPaged()`).
- Thumbnails: `PostViewModel.LoadThumbnailAsync` — max 6 concurrent (static semaphore), decode with `Bitmap.DecodeToWidth(…, 240)` on the thread pool.

## Search flow
`MainViewModel.SearchCommand` → new `SearchTabViewModel(booru, tags)` → `LoadNextPageAsync()` →
`BooruClientFactory.ClientFor(booru).SearchPostsAsync(tags, page, 100)` → filter by `TagBlacklist` → create `PostViewModel`s → thumbnails.
`SearchTabView` code-behind listens to `ScrollViewer.ScrollChangedEvent` and calls `LoadNextPageAsync` when < 600 px remain;
the tab stops when a page returns < 100 posts (`_exhausted`). Pages are 0-based in `IBooruClient`; each client converts to its server’s base.

## Download flow
`PostViewModel.ToggleQueue` / “Queue selected” → `DownloadQueue.Items.Add` (queue persisted to `download_queue` on every change) →
“Start all” → per item: skip if in `downloaded` and `SkipKnown`; render path with `FileNameTemplate` (UI thread) → reserve a unique target →
`MediaDownloader.DownloadAsync(spaced:true)` writes `<target>.part` (+`.part.meta` holding ETag/Last-Modified for resume) and renames on success →
sidecar → `downloaded` table + status Done. Failed items stay in the queue with a retry button.

## Errors
Background failures call `AppServices.ReportError` → status bar. Search errors show in the tab’s status line. Nothing throws to the UI thread unhandled
(`SearchTabViewModel.LoadNextPageAsync` and the download task catch everything) — keep it that way for new async commands.

## Viewer / reader notes
- Viewer: loads the original (`FileUrl`, else sample) through `ImageCache`, sniffs the real type, decodes with `new Bitmap(stream)`; non-image types show "no viewer" + "Open externally" (copy with an extension, allow-listed types only).
  Tag categories missing from the post are resolved by `TagCategoryResolver` (cache table `tag_categories`, then the booru's tag API).
- Reader: `ReaderViewModel` owns pages, groups (`SpreadLayout`), progress (`PoolReading.Record` with the last page shown) and a `PageBudget` (default 256 MB, setting `readerCacheMegabytes`);
  protected pages = current ±1 group and 2 ahead. `ReaderWindow` lays out paged mode in code (size depends on fit/zoom/viewport) and uses a virtualised `ItemsControl` for scroll mode.
  Stored enums: `readerMode` PAGED|SCROLL, `readerFit` PAGE|WIDTH (Java names).
- Pool archive: pages are fetched through the cache with 4 in flight, named `001.ext…` by sniffed type, `ComicInfo.xml` first; file written to `.part` then renamed.

## Media (session 3)
- `Core/Media`: `GifDecoder` (streaming, composited frames, 20 ms minimum delay), `GifEncoder` (per-frame median-cut palette), `Ugoira` (zip + `animation.json`, Java's size limits),
  `IFrameDecoder` (implemented by `App/Services/SkiaFrameDecoder`).
- `AnimationPlayer` (App): enumerates frames on a worker thread and posts one bitmap per frame to the UI; loops by enumerating again. Used by the viewer for GIF and ugoira.
- `VideoPlayback` (App): libvlc → BGRA buffer (locked by callbacks) → `WriteableBitmap` copied on a 16 ms UI timer; `VideoViewModel` adds transport controls.
- Download post-processing: `DownloadQueue.PostProcess` (set in `AppServices`) converts ugoira zips to GIF before the sidecar is written.
- `Core/Backup`: `BackupFile` (format shared with Java/mobile), `TransferLink`/`TransferServer` (one-shot, token, 5 min, 5 bad tries), `QrCode`; `App/Services/BackupActions` + `TransferViewModel/Window`.
- `Core/Logging`: `AppLog` (rotating files in `<data>/logs`, never full URLs), `LogArchive` (tar with system-info), installed in `AppServices.Create`.
