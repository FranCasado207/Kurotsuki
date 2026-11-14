# AGENTS.md — Kurotsuki-Next

Kurotsuki-Next is the **Avalonia UI (C# / .NET 10) rewrite of Kurotsuki**, a desktop booru client. The original is a JavaFX app
at `/mnt/HDD/Development/kurotsuki` (Java 25, package `pt.yaku`, version 0.6-next). It stays the **reference implementation**
until this project reaches parity: when unsure how something should behave, read the Java source.

**Read first, in this order:** this file → `brain/README.md` (index of project memory) → `TODO.md` (what to do next).

## Status

Feature parity with the JavaFX app is essentially reached: search, downloads (+ugoira→GIF), viewer (images, animated GIF, ugoira, video),
tags, library (favourites, tag groups, folders), pools (browse, reader, comic archives incl. 7z), backup export/import, phone transfer
(QR), logs, i18n en/pt, layout persistence. All on the same `config.json` / `kurotsuki.db` as the JavaFX app.
Still missing (see `TODO.md`): keyring credentials (deliberately postponed by the user), panel docking, packaging beyond a publish script.

## Layout

```
Kurotsuki.slnx
src/Kurotsuki.Core/    UI-free library: config, i18n, booru clients (+pools), downloader, queue, comic archives (zip/tar/rar/7z writers),
                       media (GIF decoder/encoder, ugoira), backup + transfer server + QR encoder, logging, library services,
                       SQLite store. No Avalonia references (images are decoded through the IFrameDecoder the app supplies).
src/Kurotsuki.App/     Avalonia app (assembly name `kurotsuki-next`): views, view models, services (AppServices, Menus, Dialogs, Shell).
tests/Kurotsuki.Core.Tests/   xUnit tests for Core (162). Fixtures/java-config.json is a config written in the Java app's format.
tests/Kurotsuki.App.Tests/    Headless UI tests (Avalonia.Headless + Skia) against an in-memory stub booru (40). Can save screenshots.
packaging/, scripts/   .desktop file, icons, scripts/publish.sh. CI: .github/workflows/ci.yml.
brain/                 Project memory for agents (architecture, decisions, Java→C# map, gotchas, session log).
```

## Commands

```bash
dotnet build                                   # whole solution
dotnet test                                    # 202 tests (Core + headless UI), must stay green
KT_SCREENSHOTS=/some/dir dotnet test tests/Kurotsuki.App.Tests   # also writes PNGs of main/viewer/reader/pools/settings
scripts/publish.sh linux-x64                   # publish/<rid>/
dotnet run --project src/Kurotsuki.App         # run the app (real config! see below)
```

**Never test against the user's real data by accident.** The app and the config manager read/write
`~/.config/kurotsuki/config.json` and `~/.local/share/kurotsuki/kurotsuki.db`, which the JavaFX app also uses. To experiment safely:

```bash
S=$(mktemp -d); mkdir -p $S/cfg/kurotsuki $S/data/kurotsuki
cp ~/.config/kurotsuki/config.json $S/cfg/kurotsuki/; cp ~/.local/share/kurotsuki/kurotsuki.db* $S/data/kurotsuki/
XDG_CONFIG_HOME=$S/cfg XDG_DATA_HOME=$S/data XDG_CACHE_HOME=$S/cache dotnet run --project src/Kurotsuki.App
```

## Hard rules

1. **Config compatibility is a contract.** `config.json`, `kurotsuki.db` (the whole schema, see `LibraryStore`) and the thumbnail
   cache file names (lowercase hex SHA-256 of the URL) are shared with the JavaFX app. Never rename a JSON field, change an
   enum's serialised name (`GELBOORU_FAMILY`…), or alter a table. `UserConfiguration` declares *every* Java field (even unused ones)
   plus `[JsonExtensionData]`, so a save from here never drops data the other app wrote. Add new fields to both sides or none.
   See `brain/config-compat.md`.
2. **Core stays UI-free.** No Avalonia types in `Kurotsuki.Core`. UI-thread dispatch is injected (`DownloadQueue` takes an `Action<Action>`).
3. **Secrets:** API keys live in `config.json` (owner-only, mode 600, written via temp file + atomic move). Never log them,
   never put them in URLs we persist or cache-key (Hydrus uses a header — see `RequestAuth`). Remote-supplied file names are
   untrusted: all paths go through `FileNameTemplate` / `UniqueTarget`, which refuse anything outside the download folder.
4. **Only open http(s) URLs** from remote data (`Shell.OpenUrl`).
5. **Don't overwrite an unreadable `config.json`.** `AppServices.LoadConfig` moves it aside. Keep that behaviour.
6. Port behaviour, not Java structure. Prefer idiomatic C# (async/await, records, `System.Text.Json`, MVVM source generators).
7. Every Core change gets a test. UI is verified by running it (see Verification).

## Conventions

- C# latest, nullable on, implicit usings, file-scoped namespaces, 4-space indent, `_camelCase` private fields.
- MVVM with CommunityToolkit.Mvvm source generators (`[ObservableProperty]`, `[RelayCommand]`); compiled bindings are on by default,
  so every `DataTemplate` needs `x:DataType`.
- Shell: icon rail (left) + one search pill (top) + inset canvas with tab chips; menu bar replaced by the ⋯ menu (`OnMoreClick`); sidebar and downloads are drawers (closed by default, downloads opens when something is queued). Look/feel: minimal, flat, rounded (8–18 px), vector icons (`Icons.axaml`, `Path.ico`; no emoji/glyph icons). Colours are theme tokens only:
  use `{DynamicResource Kt…Brush}` (Styles.axaml), never a hex literal or `StaticResource`, so Dark, Light and user themes all work.
  Six built-in themes (Dark, Light, Kuro, Frost, Moss, Sakura), contrast-checked in `ThemeTests` — keep new palettes passing it. Themes: `Core/Theming` (ThemeDefinition, ThemeStore), `Services/ThemeManager`; custom themes are JSON files in `<config>/themes/`,
  the choice is in `<config>/next-ui.json` (never in config.json — shared with Java). Appearance tab in Settings edits them live.
- UI text comes from the Java bundles (`src/Kurotsuki.Core/I18n/messages*.properties`, embedded): `Messages.Get("key", args)` in code,
  `{svc:T 'key'}` in AXAML. New keys go in **both** files under the `next.*` prefix (a test checks key parity). No new English literals.
- Context menus are built in code (`Services/Menus.cs`) so they can reflect live state; dialogs via `Services/Dialogs.cs`.
- "Save state" (Settings > General): `Core/Session/SessionStore` keeps the option and the open tabs/scroll/search box in `<config>/next-session.json` (not config.json); `MainViewModel.SaveSession` / `RestoreSession`.
- Window/panel sizes live in `config.json` `panelSizes` under `next.*` keys (`WindowLayout`); the Java app keeps them untouched.
- Comments explain *why* (server quirks, compatibility reasons), as in the Java code. No narration of what the code does.

## Verification before you say "done"

1. `dotnet build` has 0 warnings that you introduced, and `dotnet test` passes.
2. For UI changes, add/extend a headless test in `tests/Kurotsuki.App.Tests` and look at the screenshot it produces
   (`KT_SCREENSHOTS`); also launch the app against a **copy** of the config (above) when you can. Say explicitly if you could not.
   Do **not** take desktop screenshots with spectacle/import: they capture every monitor, including private content.
3. For network clients, test against the live booru if possible (Safebooru, yande.re, Konachan, e621 work anonymously; Gelbooru
   answers 401 without an API key; Danbooru answered HTTP 503 in the first live test — cause not investigated; do not spoof a browser
   User-Agent to get around it). Add a stubbed-`HttpMessageHandler` test either way (`ClientTests.cs`).
4. Update `TODO.md` (tick/add items) and append to `brain/log.md`. If you made a design decision or learned a gotcha, put it in
   `brain/decisions.md` / `brain/gotchas.md` so the next agent does not rediscover it.

## Dependencies (keep this list short)

Avalonia 12.1.5 (+Desktop, Fluent theme, Inter font, diagnostics in Debug), CommunityToolkit.Mvvm 8.4.2,
Microsoft.Data.Sqlite 10.0.14 with `SQLitePCLRaw.lib.e_sqlite3` pinned to 2.1.14 (2.1.11 has advisory NU1903 — do not unpin).
**LibVLCSharp 3.10.2** (video; Linux/macOS use the installed VLC/libvlc, `VideoLAN.LibVLC.Windows`/`.Mac` are referenced only for win/osx builds — Windows/macOS untested).
Tests: xUnit, Microsoft.NET.Test.Sdk, Avalonia.Headless + Avalonia.Skia (UI tests). Zip/tar/RAR/7z writing, GIF, QR, CRC32, the transfer
server and everything else is done with the BCL or in-repo code on purpose. Ask before adding more.

Verification tools used by tests when installed (tests skip those checks when missing): `7z`, `unrar`, `ffmpeg`/`ffprobe`, `zbarimg`, libvlc.
