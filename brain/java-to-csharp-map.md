# Java → C# map and port status

Java root: `/mnt/HDD/Development/kurotsuki/src/main/java/pt/yaku/`. Status: ✅ ported · 🟡 partial · ⬜ not started · ➖ not needed.

## Core
| Java | C# | |
|---|---|---|
| `user/config/AppPaths` | `Config/AppPaths` | ✅ (incl. legacy migration) |
| `user/config/ConfigManager`, `UserConfiguration`, `BooruConfiguration`, `BooruConfigurationAdapter`, `FolderData`, `TagBlacklist` | `Config/*` | ✅ |
| `booru/Post`, `TagCategory`, `BooruEngine/Flavor/Template`, `PostUrls`, `BooruHttp` | `Booru/Post.cs`, `Enums.cs`, `BooruHttp.cs` | ✅ |
| `booru/BooruClient` | `Booru/IBooruClient` | ✅ search, suggest, pools, tag categories (async, default interface members) |
| `booru/{gelbooru,danbooru,e621,moebooru,shimmie,szurubooru,hydrus}/*`, `dapi/*` | `Booru/Clients.cs`, `PoolClients.cs` | ✅ |
| `booru/Pool`, `PoolOrder`, `Pools` | `Booru/Pool.cs` | ✅ |
| `client/RestClientConfiguration` | `Booru/BooruClientFactory` (15 s connect / 30 s total) | ✅ (CXF → `HttpClient`) |
| `client/MediaDownloader`, `HostThrottle`, `RequestAuth` | `Net/*` | ✅ |
| `cache/CacheManager` | `Download/ImageCache`, `ImageSniffer` | ✅ (trim is not called automatically yet) |
| `download/FileNameTemplate`, `Sidecar`, `SidecarFormat`, `DownloadSettings`, `DownloadItem`, `DownloadQueue` | `Download/*` | ✅ except pool items |
| `download/BulkFetch` | `Download/BulkFetch` | ✅ |
| `download/PoolArchiver`, `comic/*` | `Download/PoolArchiver`, `Comic/*` | ✅ cbz/zip/cbt/tar/cbr/rar/7z/cb7 (7z store-only) |
| `storage/LibraryStore`, `StoreWrites` | `Storage/LibraryStore`, `Library/StoreWrites` | ✅ |
| `favorites/*`, `library/*` | `Library/*` | ✅ |
| `layout/*` | `Services/WindowLayout` | 🟡 sizes + show/hide only, no docking |
| `i18n/Messages` | `I18n/Messages` | ✅ |
| `log/AppLog`, `LogArchive`, `SystemInfo` | `Logging/*` | ✅ |
| `backup/Backup`, `TransferServer`, `TransferLink` | `Backup/*` | ✅ (ZXing replaced by an in-repo QR encoder) |
| `context/ApplicationContext` | `Services/AppServices` | ✅ (different shape on purpose) |

## UI
| Java (`ui/`) | C# | |
|---|---|---|
| `App`, `MainView`, `MainMenuBar`, `Workspace`, `Sidebar` | `Views/MainWindow` (toolbar + tabs + queue panel) | 🟡 no menu bar, no docking, no sidebar of favourites |
| `SearchTab`, `GridTab`, `ImageGrid`, `PostHoverCard` | `PostGridView`, `SearchTabViewModel`, `PostViewModel` | ✅ (hover card = tooltip) |
| `DownloadBar` | queue panel in `MainWindow` | ✅ |
| `SettingsDialog` | `Views/SettingsWindow` | ✅ |
| `PostViewer`, `TagPanel`, `ImageLoader` | `ViewerWindow`, `ViewerViewModel` | ✅ |
| `AnimatedGifView`, `UgoiraGif`, `UgoiraDecoder` | `Core/Media/*`, `Services/AnimationPlayer`, `SkiaFrameDecoder` | ✅ |
| `VideoPlayer` | `Services/VideoPlayback`, `VideoViewModel` (libvlc) | ✅ |
| `FavoritesTab`, `FoldersTab`, `FolderTab`, `TagGroupMenu`, `TagAutocomplete`, `BooruEditDialog`, `Dialogs` | `LibraryTabs.cs`, `FoldersTabView`, `Menus`, `AutoCompleteBox` in `MainWindow`, Settings, `Dialogs` | ✅ |
| `PoolsTab`, `PoolTab`, `ReadingTab`, `PoolReader`, `PageBudget`, `SpreadLayout` | `PoolViewModels.cs`, `*TabView`, `ReaderWindow`, `ReaderViewModel`, `Library/*` | ✅ |
| `BulkDownloadDialog`, `AboutDialog`, `SystemOpener` | `BulkDownloadWindow`, `AboutWindow`, `Services/Shell` | ✅ |
| `BackupActions`, `TransferDialog`, `backup/*` | `Services/BackupActions`, `TransferWindow`, `Core/Backup/*` (`BackupFile`, `TransferLink`, `TransferServer`, `QrCode`) | ✅ |
| `log/*` | `Core/Logging/*` | ✅ |

## Java tests worth porting alongside features
`FileNameTemplateTest` ✅(ported in spirit), `SidecarTest` ✅, `HostThrottleTest` ✅, `MediaDownloaderTest` ⬜ (needs a local HTTP server — use Kestrel/`HttpListener`),
`DownloadQueueTest` ⬜, `LibraryStoreTest` 🟡, `BackupTest`/`TransferTest` ⬜, `PoolArchiverTest` ⬜, `PageBudgetTest`/`SpreadLayoutTest` ⬜, `MessagesTest` ⬜.

## Porting checklist (per feature)
1. Read the Java class + its test. 2. Port logic into Core (UI-free) with tests. 3. Check shared-file impact (`config-compat.md`).
4. Add view model + view. 5. Run against a copy of real data. 6. Update this table, `TODO.md`, `log.md`.
