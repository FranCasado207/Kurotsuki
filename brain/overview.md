# Overview

**Kurotsuki** is a desktop booru client (browse/search image boards, queue and download posts, read pools as comics, keep
favourites). The author (Francisco, GitHub `FranCasado207`) wrote it in JavaFX (`/mnt/HDD/Development/kurotsuki`, Maven, Java 25,
package `pt.yaku`, AtlantaFX theme, CXF REST client, sqlite-jdbc, Gson). **Kurotsuki-Next** is the rewrite in Avalonia UI
(C#/.NET 10) at `/mnt/HDD/Development/Kurotsuki-Next`. A companion Android app lives in `/mnt/HDD/Development/kurotsuki-mobile`
(only relevant for the "send to phone" transfer feature).

## Goal
Reach feature parity with the JavaFX app on a modern, lighter stack — while both apps keep working side by side on the same
user data (same `config.json`, same `kurotsuki.db`, same cache) so the user can switch freely during the migration.

## Plan
1. **Base (done):** search + downloads, same config. ← current state
2. Polish the base (autocomplete, bulk download, i18n, layout persistence) — see `TODO.md` “Next up”.
3. Viewer, tags, library (favourites, groups, folders).
4. Pools + reader + comic archives.
5. Backup / phone transfer / logs, packaging (Arch PKGBUILD, Windows), CI.

## What the user asked for (original brief)
“Migrate kurotsuki from JavaFX to a modern AvaloniaUI project. I already have all the dependencies needed; make a base first with
just search and downloads, make it use the same config as the JavaFX project. After that, make a TODO.md, an AGENTS.md and a
brain, so that future agents can continue the project. Call it Kurotsuki-Next.”

## State at end of session 3 (2026-11-07)
Feature parity with the JavaFX app is essentially reached (see `TODO.md` "Done"/"Added in session 3"). `dotnet build` is warning-free; 117 tests pass (103 Core, 14 headless UI).
- Verified with independent tools where available: 7z, unrar, ffmpeg/ffprobe, zbarimg, libvlc, real sockets; real app starts on a copy of the user's config; `LibraryStore` reads the user's real Java-written DB.
- Not verified: a human looking at it on a real display, sound, Windows/macOS, a phone scanning the QR, Danbooru/Hydrus/Szurubooru/Shimmie live.
- Credentials-in-keyring was explicitly postponed by the user. Nothing is committed (`git init` only).

## User preferences observed
- Wants things done end to end with minimal back-and-forth; infrastructure for future agents (TODO/AGENTS/brain) is part of the deliverable.
- Default pronouns: they/them. Work happens on CachyOS (Linux, Wayland+X), shell fish/zsh.
