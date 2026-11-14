# Session log

## 2026-11-07 — Session 1: base migration
- Read the Java project (config, booru clients, download queue/downloader, store, search tab) and mapped it.
- Created `Kurotsuki-Next` (solution, Core, App, Tests); ported config (exact `config.json` format), 7 booru clients (search + suggest), downloader
  (resume/retry/backoff/throttle), filename templates, sidecars, SQLite history/queue, thumbnail cache.
- Built the Avalonia UI: toolbar (booru picker, tag box), search tabs with infinite scroll, thumbnail grid + context menu, download queue panel, Settings window.
- Verification: `dotnet test` 29/29; app launched 12 s against a copy of the real config without errors; live search on 5 boorus OK, Danbooru 503; real download + sidecar + history OK.
- Found/fixed: NU1903 on SQLite native lib (pinned 2.1.14); `Uri.ToString()` unescaping in a test; Avalonia 12 clipboard extension namespace; `Watermark` → `PlaceholderText`.
- Tip: `spectacle -b -n -f -o file.png` works for screenshots but captures ALL monitors/the whole desktop (private content) — use `-a`/`-r` or delete the file right after viewing.
- **Not verified:** the grid/tabs/Settings visuals (only the empty main window was seen), Hydrus/Szurubooru/Shimmie live, behaviour on Windows/macOS.
- Next agent: start with `TODO.md` → “Next up”. First, run the app on a copy of the config and visually check the layout.

## 2026-11-07 — Session 2: polish + viewer/tags/library/pools
- Polish: autocomplete, bulk download, i18n (Java bundles), layout persistence, queue UI, error hints, cache trim/clear, CI, publish script, `.desktop`, icons.
- Core: full `LibraryStore` schema, library services, pools for e621/Danbooru/Moebooru, tag-category lookup, comic archives (zip/tar/RAR) + ComicInfo, `BulkFetch`, `IMediaDownloader`.
- App: viewer, tag panel/groups, favourites/folders tabs, pools tab/grid/reader, reading tab, settings tabs, sidebar, menu bar, About.
- Bugs found by the new tests and fixed: "saved" badge stale after a download (added `DownloadQueue.DownloadedChanged`); reader blank when the pool loaded before the window subscribed
  (`RebuildPaged()` at construction); wrong message key used for a Settings heading; flaky queue test (inline `post` now serialised); bundle key drift caught by the parity test.
- Safety: a command probing Danbooru with a browser User-Agent was denied by the environment; I dropped it (see D-16). A desktop screenshot also captured private content in session 1 — use headless captures.
- Next: `TODO.md` "Next up". Ask the user before the keyring, video library, or 7z library choices.

## 2026-11-07 — Session 3: right-click fix + video/ugoira/GIF/7z/backup/transfer/logs
- Fixed right-click on tiles (menus now built on `ContextRequested`); test sends a real right-button press.
- Added: GIF decoder/encoder, ugoira (view + download→GIF), libvlc video player, 7z writer, backup export/import, transfer server + QR encoder + window, logging + "Export logs",
  Hydrus autocomplete, legacy folder migration, cache trim at start. Renamed `Backup` class to `BackupFile` (clashed with its namespace).
- Verification with independent tools: `7z t`/`x`, `unrar t`/`x`, `ffprobe` (our GIFs), ffmpeg-made GIFs/MP4 (our decoder/player), `zbarimg` (our QR codes, incl. the app's own bitmap), real sockets for the transfer server. 117 tests pass.
- Bugs found by those checks: QR alignment patterns on the timing lines skipped for version ≥ 7; libvlc padded frame size; wrong 7z number test expectation (code was right).
- New dependency: LibVLCSharp (D-17). Not tested: Windows/macOS, sound, a phone scanning the QR, Danbooru/Hydrus/Szurubooru/Shimmie live.

## 2026-11-07 — Session 4: visual redesign + themes
- Rebuilt the look: neutral flat palette, rounded controls, hover-only tile captions, accent-underlined tabs, vector icons (`Icons.axaml`) replacing glyph/emoji buttons.
- Theme system: Dark, Light and user themes (JSON in `<config>/themes/`, selection in `<config>/next-ui.json`). `ThemeManager` recolours the `Kt*Brush` resources in place and rebuilds Fluent's palette. Settings → Appearance edits live and reverts on cancel.
- Tests: `ThemeTests` (Core), `ThemeUiTests` (switching, custom save, revert). Screenshots checked for Dark, Light, custom accent, settings. 124 tests pass.
- Gotcha: zero-height/width geometries (a plain minus line, `h.01` dots) collapse under `Stretch=Uniform`; draw them as a thin closed shape.
- Not verified: the redesign on a real display, Windows/macOS font rendering, and the tag-colour brushes of already-open viewers when the theme changes (they update on the next open).
- Palette pass: new Dark (ink + violet) and Light (paper + indigo), plus Kuro (AMOLED/amber), Frost, Moss, Sakura. Accent buttons now change colour on hover/press (`KtAccentHoverBrush`/`PressedBrush`) instead of fading; disabled buttons have an explicit readable style. `ThemeContrastTests` enforces WCAG ratios for every built-in. 130 tests pass.

## 2026-11-07 — Session 4 (cont.): polish pass
- Fixed double-click opening the wrong post (handler used the Toggle-selection, now the tile under the pointer; same in pools list).
- Thumbnail size slider + Ctrl+wheel (`grid.tile` in panelSizes, shared by all grids); "queued" badge hidden once saved; status bar hidden when empty; icon empty states; F11 fullscreen in the viewer. 133 tests pass.

## 2026-11-07 — Session 4 (cont.): zoom fix + search history
- Viewer zoom: zoomed images used `Stretch.None`, which ignores Width/Height (drew at natural size, clipped). Now `Stretch.Fill` with explicit size; the first zoom step starts from the fitted scale (`ViewerViewModel.FitScale`, set by the window) instead of jumping to 100%.
- Search history (new; the Java app has none): `Core/Library/SearchHistory`, `<data>/search-history.json`, newest first, 30 max, clock button beside the search box → menu + "Clear history".
- Gotcha (tests): two `ImageCache` instances on the same folder fetching the same URL collide on the `.part` file. UI tests that start a search must wait for their thumbnails before ending, or they break parallel tests.

## 2026-11-07 — Session 4 (cont.): blacklist report
- Search grids now keep what the blacklist hid (`PostGridViewModel.Hidden`, with the matching tags). "Review hidden" (grid bottom bar) opens `BlacklistReportWindow`: thumbnails, which tag blocked each post, "Show anyway" / "Show all" / "Open" in the viewer.
- "Show anyway" is remembered per post (`Core/Library/BlacklistExceptions` → `<data>/blacklist-allowed.json`, key = `Post.Key`), honoured by search, reader and bulk download via `TagBlacklist.IsBlocked(post, set, exceptions)`. Settings → Blocked tags shows the count and "Hide them again". The tag itself stays blacklisted; config.json/db untouched.
- Hidden count used to be overwritten on every page; it now accumulates. 140 tests pass.

## Session 5 — shell redesign
Dropped the JavaFX-shaped layout (menu bar, sidebar with search, boxed tabs, status bar). New shell: left icon rail (Search/Favorites/Folders/Pools/Reading + Tags drawer), top bar with brand mark, one search pill (booru picker, query, history, search), downloads button with count badge and thin progress line, ⋯ menu holding the old menu items. Content sits on an inset rounded canvas with tab chips; status text lives in the chip row (or under the welcome text). Sidebar and downloads are drawers, closed by default; adding to the queue opens the downloads drawer. Verified with headless screenshots (Dark, Sakura, Kuro); 140 tests green; not checked on a real display.

## Session 5b — dialogs and icon geometry
Settings: title, pill tabs (Fluent's underline pipe brushes overridden), content on one rounded sheet, footer bar. Message/prompt/confirm dialogs, About, Transfer, Bulk got larger padding and an accent primary button. Icons: plus was asymmetric (filled rect + stroke), fixed; every icon now starts with an invisible "M2 2M22 22" so all bounds are the same square (gotcha: a Uniform-stretched Path is top-left aligned in its box, so non-square icons like chevrons/dots/minus sat off-centre).

## Session 5c — queue drawer, pools in the search pill, tile hover
Removed the top-bar brand icon and the thin progress line (its track was darker than the background). Downloads drawer redesigned (title + close, overall progress card, Start all + icon buttons, one card per item, failed in red). More spacing between downloads and ⋯. Pools rail button removed: a "Pools" chip in the search pill (`MainViewModel.PoolMode`) makes Search open the pools browser with that query. Tile hover: picture eases to 1.06, gradient caption and quick queue/favourite buttons fade in; no hover border.

## Session 5d — hover card
Post tooltip is now a card: outlined rating chip (success/accent/danger tokens), score, dimensions, first 12 tags as chips, '+N more'. Global ToolTip style (raised surface, rounded). Verified in a headless screenshot (tile-tooltip).

## Session 6 — tray progress icon
`Services/DownloadTray` (TrayIcon, created in App.OnFrameworkInitializationCompleted, not in tests) shows a ring + arrow icon while any item is Downloading, tooltip/menu "Downloading: x of y done (p%)", click or "Show window" raises the main window; on finish it shows a green check for 4 s then hides. Progress counts only started items (`Measure`), so idle queued posts don't distort it. `TrayIconRenderer` draws it from the active theme's tokens (64 px RenderTargetBitmap). Verified: unit test for the maths, headless render of all states in 3 themes, app starts with the tray without errors. NOT verified: the icon in a real KDE/Windows tray while downloading.
2.1.0: tray progress icon, arch package

## Session 6b — tabs keep their view
Switching tabs recreated the view (ContentControl), so the grid scrolled back to the top. The main window now hosts one live view per tab (ItemsControl over Tabs, visibility via TabViewModel.IsSelected). Test: TabScrollTests.

## Session 7 — "Test" before adding a booru
- Settings > Boorus: the Add button is disabled until a Test succeeds (one `SearchPostsAsync(null, 0, 1)` through the real client). Editing kind/URL/user/key resets the test. Test: `AddingABooruNeedsASuccessfulTest`.
- Known flake: `DoubleClickTests.DoubleClickOpensTheTileUnderThePointerEvenWhenAnotherIsSelected` fails now and then under parallel runs, passes alone (not investigated).

## Session 8 — Save state
- Settings > General "Save state": off by default. When on, `next-session.json` (Core/Session/SessionStore) stores open tabs (search, pools, pool, favorites, folders, folder, reading), the selected tab, the booru, the search box, pool mode and each grid's scroll offset. Saved on tab changes and at exit; turning it off deletes the saved state.
- Scroll restore: `PostGridViewModel.PendingScroll`; the view loads more pages until the grid is tall enough, then jumps. Unvisited tabs keep their pending value (`SavedScroll`).
- Not restored: window position, open viewer/reader windows, selected posts, scroll of the Pools list. Tabs of a removed booru/folder are skipped.
- Tests: `SessionTests` (Core), `SaveStateReopensTheSameTabsAndScrollPosition` (UI).

## Session 9 — TODO pass
- Rating filter: `PostGridViewModel.ShowSafe/Questionable/Explicit` + `PostViewModel.Shown`; tiles collapse through a `ListBoxItem` style (`IsVisible={Binding Shown}`); viewer, "Queue all" and "Queue selected" only act on `ShownPosts`. Posts with no known rating always pass. Test `RatingFilterHidesTilesFromTheGridViewerAndQueue`.
- Gotcha: UI tests share one persisted download queue (same temp DB), so a test that queues posts must remove them again and must not assert on the total count.
- i18n: `next.customKind` replaces the English "Custom: …" labels. Danbooru 503 closed (maintenance). TODO ticks: Arch package, tab persistence.

## Session 10 — TODO batch (2.3.0)
- Done: safe mode per booru, saved searches, sort loaded results, reader zoom/bookmarks/"Next pool" (`PoolSeries`), tag info (Danbooru/e621), parent/children, reverse image search, video volume/fullscreen/shift+arrows, animated WebP (Skia) and APNG (`ApngDecoder`), merged multi-booru search. Prefs live in `next-settings.json` (`NextSettings`).
- Skipped on purpose: IPv6/QR>10 in transfer (phone format is IPv4 only). Not done: virtual grid, panel docking, video pages in the reader.
- KNOWN FLAKE (not solved): about half of the full UI-suite runs have 2-4 tests time out waiting for thumbnails (posts=7, thumbs=0, failed=0, i.e. loads stuck, not failed). Also happens without the newest tests. Suspect: thread-pool starvation or stuck thumbnail slots (`PostViewModel.ThumbnailSlots`, 6, static). Untested idea: `ThreadPool.SetMinThreads(64, 64)` in `Harness.Init`. Tests pass alone and with TMPDIR set to another folder. WaitFor default raised to 20 s; `ImageCache` in-flight map is now static; `StubBooru.Files` is concurrent.

## Session 11 — ideas batch (2.4.0)
- New: `PostLink` (pasted links/ids), `DuplicateIndex` (md5 file `next-downloaded-hashes.txt`, used by `DownloadQueue.Duplicates`), `LibraryReport` (stats, CSV with formula defusing), watched searches in `NextSettings`, `CommandPaletteWindow`, privacy blur style, `MainViewModel.Extras.cs`.
- Theme: `next-ui.json` now holds `{theme, followSystem}`; `ThemeStore.Select` keeps the flag. Text size: `KtBaseFontSize` + `ControlContentThemeFontSize` resources.
- Gotcha: a new tab type needs a DataTemplate in MainWindow.axaml (the merged search tab had none until now).
- The flaky UI suite is still unsolved. `ThreadPool.SetMinThreads(64, 64)` in `Harness.Init` did NOT fix it. Facts: failing runs show posts=7, thumbs=0, failed=0 (loads stuck); same tests pass alone; runs with TMPDIR set to a private folder passed 24/25.
