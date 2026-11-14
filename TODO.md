# TODO — Kurotsuki-Next

Port status of the JavaFX app (`/mnt/HDD/Development/kurotsuki`). Effort: S = hours, M = a day or two, L = several days.
Tick items when done, and record anything non-obvious in `brain/`. Java file names are given so you know where to look.

## Done
**Base:** solution + CI, same `config.json`/`kurotsuki.db`/cache, 7 booru clients, search tabs with infinite scroll, download queue
(resume, retry, throttle, templates, sidecars, skip-known, persistence), settings.

**Polish:** tag autocomplete (`AutoCompleteBox`, last word, `-tag` aware) · clear hint for HTTP 401/403/503 (add credentials) · queue UI
(open folder/file, retry, overall progress) · bulk download of pages N..M (`BulkFetch`, max 1000 posts/50 pages) · i18n en/pt from the Java
bundles · window/sidebar/queue size persistence · menu bar, About · cache size/clear/trim · publish script, `.desktop`, icons, CI ·
`DownloadQueue` tests (via `IMediaDownloader`) · headless UI tests.

**Viewer / tags:** viewer window (zoom, fit, prev/next, keyboard, favourite, download, folder menu, pools menu, open in browser/externally),
tag panel coloured by category (categories fetched + cached for Gelbooru/Moebooru/Szurubooru), favourite tags, tag groups (sidebar,
create/rename/delete, group search), tooltip "hover card".

**Library:** favourite posts, folders, favourite tags, tag groups; full Java schema in `LibraryStore`; one-time import of old
config.json favourites (same flag as Java).

**Pools:** e621, Danbooru, Moebooru clients (search, get, post order, covers, pools of a post) · pools tab (covers, filters, reading status) ·
pool grid tab · reader (paged + scroll, spread, RTL, fit page/width, zoom 1–5×, drag pan, memory budget, blacklist option, progress,
mark finished) · "Continue reading" tab (opens at start when something is in progress) · pool download as cbz/zip/cbt/tar/cbr/rar
with ComicInfo.xml · `%group%` filename token.

## Not verified by a human yet (headless tests + screenshots only)
- Look and feel on a real display: sidebar, tabs, context menus, dialogs, drag-to-pan, mouse wheel page turning, Ctrl+wheel zoom, video sound/fullscreen behaviour, the QR scanned by an actual phone.
- Danbooru live (503 seen), Hydrus, Szurubooru, Shimmie live. Windows/macOS.
- Opening files externally (`Shell.OpenFile` allow-list), real image decode of huge pages (6000 px cap).

## Added in session 3 (all verified with real tools where available)
- Right-click on tiles/sidebar tags/viewer tags (menus are built on `ContextRequested`; headless test sends a real right-click).
- Animated GIF playback (own streaming decoder, checked against ffmpeg-made GIFs) and GIF encoder (checked with ffprobe).
- Ugoira: viewer plays the zip (Skia decoder + `animation.json` delays); downloads are converted to GIF (zip removed, failure keeps the zip and says so).
- Video in the viewer via libvlc (play/pause, ±10 s, seek, mute); falls back to "Open externally" when libvlc is missing.
- 7z/cb7 pool archives (store-only writer; `7z t`/`7z x` verified); RAR verified with real `unrar`.
- Backup export/import (the Java sample file imports), "Send to phone" with an in-repo QR encoder (decoded by `zbarimg` at versions 1–10)
  and a hand-rolled one-shot HTTP server, log files + "Export logs", Hydrus tag autocomplete, legacy `~/kurotsuki` migration, cache trim at start.

- [x] Visual redesign (minimal, flat, vector icons) with Dark / Light / custom themes (Settings → Appearance).

## Next up
- [ ] **Credentials in the OS keyring** — explicitly postponed by the user. Needs a compatibility story with the Java app. M
- [ ] **Windows/macOS**: nothing has been run there. LibVLC natives come from NuGet on those builds (untested); `AppPaths` still uses `~/kurotsuki` there (change both apps together). M
- [x] Video: fullscreen button / double-click / F11, volume slider, Shift+←/→ seek, ↑/↓ volume, M mute. Open: video pages in the reader; hardware decoding options; libvlc 4.
- [x] Animated WebP (Skia codec, `SkiaAnimation`) and APNG (own `ApngDecoder`: the SkiaSharp build ignores APNG frames); played in the viewer like GIF.
- [x] Arch `PKGBUILD` (`scripts/arch-package.sh`, package `KuroTsukiV2`, depends on `vlc`). Still open: a Windows zip/installer; self-contained single-file publish is untested. S
- [x] i18n: "Custom: …" booru kinds (`next.customKind`). Open: engine labels, a few tooltips; language switch needs a restart (as in Java). S
- [x] Danbooru anonymous 503: was the site maintenance (user confirmed); on 2026-11-13 `posts.json` answers 200 anonymously with the app's own User-Agent. The credentials hint stays for real 401/403.
- [x] Reader: zoom remembered per pool, bookmarks (B key, Bookmarks menu), "Next pool" button (name-based guess, `PoolSeries`). All in `next-settings.json`.
- [ ] Panel docking like Java (`layout/*`: panels movable to LEFT/RIGHT/TOP/BOTTOM/HIDDEN). Only show/hide + sizes now. L
- [ ] Thumbnails: virtualise the grid (`WrapPanel` realises every loaded tile) and unload off-screen bitmaps. M
- [ ] Transfer: mDNS discovery. IPv6 and QR versions above 10 were considered and dropped: the phone format (Java docs/backup-format.md) is IPv4 only, and the link fits QR version 5.
- [x] Persist open tabs: Settings > General "Save state" (`next-session.json`; tabs, selected tab, scroll, search box).

## Search ideas carried over from the Java TODO (not built there either)
- [x] Search history (clock button) and a rating filter for loaded results (Safe/Questionable/Explicit chips under every grid).
- [x] Saved searches (history menu), multi-booru merged search ("All" chip, `MergedSearchTabViewModel`), per-booru safe mode (⋯ menu), sort loaded results (sort button).
- [ ] Moebooru pool covers (one request per pool), Danbooru "pools of a post" in the viewer is done (via pool search); reader bookmarks. S–M
- [x] Tag info (wiki, aliases, implications: Danbooru and e621; tag context menu in the viewer), parent/children ("Related posts"), reverse image search menu (SauceNAO, IQDB, Google Lens, TinEye, ascii2d).

## Ideas batch (session 11)
- [x] Paste a post link or id in the search box (opens the viewer) · watched searches with "new" counts · "Watch later" folder in the post menu · filter box in every grid (Ctrl+Shift+F) · command palette (Ctrl+K) · privacy mode (F9) · slideshow (P) · library statistics · favourites CSV export · duplicate files across boorus skipped (`DuplicateIndex`) · follow system dark/light · text size · "Add a booru" button on first run.
- [ ] Not done: download schedule / speed limit, per-tag folder rules, auto-filling folders, side-by-side compare, rotate image, preload the next pool, video in the reader, "already in a folder" mark on tiles, cache/queue self-repair.
- [ ] **Flaky UI suite**: about half of the full runs have 2-4 tests time out waiting for thumbnails (see brain/log.md, Session 10/11). Passes alone and with TMPDIR pointing elsewhere.

## Known gaps / risks
- `SuggestTagsAsync` is a stub for Shimmie and Hydrus (same as Java).
- Thumbnails are decoded with Skia: videos and ugoira zips show "No preview" in the grid (the viewer plays them).
- Tag blacklist applies to search results, bulk download and (optionally) the reader; not to favourites/folders/pool grids.
- The viewer shows one window at a time (opening another post replaces it).
- Reader scroll mode estimates page height from the API's width/height until a page is decoded.
