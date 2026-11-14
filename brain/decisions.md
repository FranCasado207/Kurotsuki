# Decision log

Format: **D-nn — title** · date · status. Context → decision → consequences. Add new entries at the bottom; supersede instead of rewriting.

**D-01 — Two projects, Core + App** · 2026-11-07 · accepted
Context: Java code mixed JavaFX properties into domain classes (`DownloadItem`). Decision: `Kurotsuki.Core` has no Avalonia dependency (uses CommunityToolkit.Mvvm’s `ObservableObject`, which is UI-agnostic). Consequence: testable without a UI; a future CLI/mobile head could reuse Core.

**D-02 — Share `config.json`, `kurotsuki.db` and the cache with the JavaFX app** · 2026-11-07 · accepted (user requirement: “same config”)
Decision: identical locations/format; extension-data round trip; queue writes only touch `kind='post'`. See `config-compat.md`. Consequence: the C# types mirror Java’s field names even where C# naming would differ (handled by camelCase policy).

**D-03 — `System.Text.Json`, no Newtonsoft/Gson-alike** · 2026-11-07 · accepted
Parsing of booru responses uses `JsonDocument` with tolerant accessors (`JsonExt`) instead of DTO classes: servers mix numbers/strings/null and the Java code was equally manual for most engines. Config/Post use typed serialisation.

**D-04 — Add `Microsoft.Data.Sqlite`** · 2026-11-07 · accepted, **flag to user**
The user said all dependencies were already available; their local NuGet cache had no SQLite or xUnit. Sharing the Java app’s download history/queue requires SQLite, and tests need xUnit, so both were added (online restore). Alternatives: a JSON file for history (would fork the data from the Java app) — rejected. SQLite native lib pinned to patched 2.1.14.

**D-05 — No DI container, no navigation framework yet** · 2026-11-07 · accepted
`AppServices` + constructor injection is enough for the base. Revisit when the number of services/windows grows (viewer, reader, library).

**D-06 — Plain `HttpClient`, hand-written URL builder (`ApiRequest`)** · 2026-11-07 · accepted
Replaces CXF/JAX-RS proxies. Query values are percent-escaped with `Uri.EscapeDataString`; null values omitted like JAX-RS did.

**D-07 — Queue persistence is write-through on the UI thread** · 2026-11-07 · accepted (revisit)
Simple and correct for a few hundred items; `SaveQueue` coalesces calls within one UI-loop turn. If queues of thousands of items get slow, batch with a timer or move to a background channel.

**D-08 — UI text is English literals for the base** · 2026-11-07 · open
i18n is the first “next up” polish item; do it before adding many more strings.

**D-09 — Reuse the Java i18n bundles as embedded resources** · 2026-11-07 · accepted
Keys, `{0}` placeholders and translations are shared; new keys use the `next.*` prefix in both files (parity is tested). `Messages` implements the subset of Java `MessageFormat` the bundles use. Language change needs a restart (as in Java). XAML uses `{svc:T 'key'}`.

**D-10 — Library state in SQLite only; services are UI-free and write-through** · 2026-11-07 · accepted
`Favorites`, `TagGroups`, `PostFolders`, `PoolReading` live in Core, change memory first and undo on a failed write. If the DB cannot be opened the app falls back to an in-memory DB and says so.

**D-11 — Pools via default interface members on `IBooruClient`; clients as partial classes** · 2026-11-07 · accepted
Keeps the per-engine pool code next to the engine's client without forcing Shimmie/Szurubooru/Hydrus/Gelbooru to implement it.

**D-12 — Comic archives without new dependencies** · 2026-11-07 · accepted
zip = `System.IO.Compression`, tar = `System.Formats.Tar`, RAR = port of the Java store-only writer with an in-repo CRC32. 7z/cb7 refused with a clear message until a library is approved.

**D-13 — Layout stored in `panelSizes` under `next.*`** · 2026-11-07 · accepted
Shares config.json with Java without breaking it (verified against `PanelLayout.java`). No dock model; panels are show/hide + width.

**D-14 — Headless UI tests with a stub booru** · 2026-11-07 · accepted
`AppServices.Create(apiClient, mediaClient)` accepts HTTP clients; `StubBooru` serves a Danbooru-style API (+ pool) and PNGs. Added `Avalonia.Headless`/`Avalonia.Skia` (already in the local NuGet cache) for tests only.

**D-15 — Credentials keyring postponed** · 2026-11-07 · user decision. Do not start it without asking.

**D-16 — No spoofed User-Agent / anti-bot evasion for Danbooru** · 2026-11-07 · accepted
A probe with a browser UA was (rightly) blocked by the environment's safety check. The app identifies itself honestly; the 503 is handled with a hint to add credentials.

**D-17 — Add LibVLCSharp for video** · 2026-11-07 · accepted, **flag to user** (user asked for video; the local NuGet cache had no video library)
Frame-callback rendering (BGRA buffer → `WriteableBitmap`) instead of a native control: independent of Avalonia internals and testable headless. Linux/macOS rely on the installed libvlc; Windows builds reference `VideoLAN.LibVLC.Windows` (not tested). Failure to load libvlc → "Open externally".

**D-18 — Core stays image-library-free: `IFrameDecoder`** · 2026-11-07 · accepted
Ugoira/GIF code lives in Core; JPEG/PNG/WebP decoding is injected (`SkiaFrameDecoder` in the app, a raw-RGBA fake in tests).

**D-19 — Own QR encoder and one-shot HTTP server instead of ZXing / HttpListener** · 2026-11-07 · accepted
The link is short (byte mode, M, versions 1–10) and tests decode it with `zbarimg`. `TcpListener` needs no admin rights on Windows (HttpListener on `+` does).

**D-20 — 7z store-only** · 2026-11-07 · accepted. Pages are images; no LZMA needed. Verified with `7z t`/`x`.

**D-21 — Ugoira conversion failure keeps the zip** · 2026-11-07 · accepted (differs from Java, which marked the item failed). The file is still useful; the user is told.

**D-22 — Themes are token files, selection kept outside config.json** · 2026-11-07 · accepted
Ten colour tokens + a dark/light base; missing tokens fall back to the base. The choice lives in `next-ui.json` so the config shared with the Java app gains no unknown field. Brushes are mutated in place (not swapped) so any resource lookup follows. Hex literals in views are banned (see AGENTS.md).
