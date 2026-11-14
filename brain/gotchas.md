# Gotchas

## Avalonia 12 / .NET 10
- `TextBox.Watermark` is obsolete → use `PlaceholderText`.
- Clipboard: `IClipboard.SetTextAsync` is an **extension method** in `Avalonia.Input.Platform` (add the `using`).
- Compiled bindings are the default (`AvaloniaUseCompiledBindingsByDefault` in the csproj): every `DataTemplate` needs `x:DataType`; to reach the
  parent view model from inside an item template use `$parent[ListBox].((vm:MainViewModel)DataContext).SomeCommand`.
- Theme palette override: `<FluentTheme><FluentTheme.Palettes><ColorPaletteResources x:Key="Dark" Accent=… RegionColor=… />` (compiles; visually unverified).
- Asset URIs use the **assembly name**: `avares://kurotsuki-next/…` (`AssemblyName` is `kurotsuki-next`, not the project name).
- `AvaloniaUI.DiagnosticsSupport` (dev tools, F12) is Debug-only via the csproj condition.
- Scroll-to-load: the inner `ScrollViewer` of a `ListBox` raises `ScrollViewer.ScrollChangedEvent`, which bubbles — handled on the `UserControl` with `AddHandler`.
- `Bitmap.DecodeToWidth(stream, width)` keeps memory low for thumbnails; do it off the UI thread.

- `Window.DataTemplates` with `DataType=` picks the tab view by view-model type; the view's own `x:DataType` can be a base class (`PostGridView` ← `PostGridViewModel`).
- `AutoCompleteBox`: `AsyncPopulator`, `TextSelector` (return the full text to put in the box), `FilterMode="None"`; Enter must be handled manually when the drop-down is closed.
- Context menus: build in the `ContextMenu.Opening` handler from `PlacementTarget.DataContext` (`Services/Menus.cs`); an `ItemsSource` of controls works.
- `ScrollBarVisibility` lives in `Avalonia.Controls.Primitives`; `ToolTip.Tip`/`ShowDelay` are attached properties.
- A scroll viewer whose content must *fit* needs its scroll bars set to Disabled, otherwise the child is measured unbounded (viewer fit mode toggles this in code-behind).
- Headless tests: `HeadlessUnitTestSession.StartNew(typeof(TestApp)).Dispatch(...)`; `UseSkia()` + `UseHeadless(UseHeadlessDrawing=false)` is needed for real frames;
  `HeadlessWindowExtensions.CaptureRenderedFrame(window)` gives a bitmap. There is no desktop lifetime, so `Shell.MainWindow` is null (dialogs/owners must tolerate that).
  Set the `XDG_*` variables in a `[ModuleInitializer]` before `AppPaths` is first read, or the tests touch the real `~/.config`.
- `Bitmap.Save(string)` is obsolete (warning in the test harness).

- **Right-click on `ListBox` items**: filling a XAML `ContextMenu` inside `Opening` showed nothing (an empty menu never opens). Handle `ContextRequested` on the item
  and open a freshly built menu (`Menus.Show`). Verified with `HeadlessWindowExtensions.MouseDown/MouseUp(…, MouseButton.Right)`. `ContextRequested` is raised after the press/release.
- **libvlc via LibVLCSharp**: `Core.Initialize()` clashes with the `Kurotsuki.Core` namespace (write `LibVLCSharp.Shared.Core`); the video-cleanup callback takes `ref IntPtr opaque`;
  the format callback gets the *coded* size (a 160×120 video came as 160×130): crop to `MediaPlayer.Size(0, …)`. RV32 = BGRA. Use `--no-xlib`; tests set `KT_VIDEO_NO_AUDIO=1` (`--no-audio`).
- A `WriteableBitmap` updated in place needs the `Image` to be invalidated (`VideoViewModel.Redraw`); `AnimationPlayer` hands out a new bitmap per frame instead.
- GIF: encoder code size grows when `next > 1 << codeSize` (one entry ahead of the decoder); delays under 20 ms are raised to 20 ms (as Java).
- QR: alignment patterns on the timing lines (version ≥ 7) are real; only the three that hit finder patterns are skipped. Uneven blocks start at version 8-M.
- 7z numbers: first byte's leading 1-bits = number of extra bytes (0x010302 → C1 02 03). One folder per file with the Copy coder keeps the header simple.
- `Bitmap.Save(Stream)` is obsolete in Avalonia 12 (pragma'd in the tests).

## .NET / library
- `Uri.ToString()` **unescapes** (`%20` → space). Assert on `AbsoluteUri` in tests, send `AbsoluteUri`/the original string on the wire.
- `JsonNamingPolicy.SnakeCaseUpper` (net8+) is what makes C# enum names match Java’s `UPPER_SNAKE` constants.
- `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 (pulled by Microsoft.Data.Sqlite) triggers NU1903; it is pinned to 2.1.14 in `Kurotsuki.Core.csproj`.
- xUnit needs `<Using Include="Xunit" />` here (the template that normally adds it was not used). `InternalsVisibleTo` Core → `Kurotsuki.Core.Tests` is declared in the Core csproj.
- `File.Move(src, dst, overwrite: true)` is the atomic replace used for config and downloads (same directory ⇒ same filesystem).
- `HttpClient.Timeout` covers the *whole* request including body; `MediaDownloader` therefore uses `InfiniteTimeSpan` with its own header timeout (60 s) and stall watchdog (30 s without data).

- `Assert.DoesNotContain("\u0001", s)` is culture-sensitive and matches ignorable characters: compare whole strings instead.
- SQLite foreign keys are enforced (`PRAGMA foreign_keys = ON` at open): adding a non-favourite tag to a group throws, as in Java (`SqliteException`).
- `Uri` percent-encoding of `search[name_matches]` is `search%5Bname_matches%5D`; tests assert on `AbsoluteUri`.
- Gelbooru's API answers 401 without `api_key`+`user_id` (it worked in the first live test only because the user's config had them).
- Shell `cwd` resets between tool calls; never `pkill -f` a pattern that matches your own command line (it killed the shell once).

## Behaviour carried over from Java (don’t “fix”)
- Safebooru/Gelbooru/Moebooru posts carry no tag categories, so `%artist%` renders `unknown` for them. Same in Java.
- Download target names are made unique with ` (n)` suffixes rather than overwritten; with `skipKnown` on, re-queuing a saved post becomes **Skipped**.
- A queue item that fails keeps its `.part` file only if the server sent an ETag/Last-Modified (else it is discarded) so resume never splices different files.
- Credentials sent over plain `http` to non-local hosts only produce a warning in Settings (Java logs a warning on load).

## Environment
- Machine: CachyOS, Wayland (`WAYLAND_DISPLAY=wayland-1`) and X (`DISPLAY=:0`), .NET SDK 10.0.113. NuGet restores fine online; the user’s offline cache already has Avalonia 12.1.5, Fluent theme, Inter font, MVVM Toolkit 8.4.2.
- The tool `Read`/shell cwd resets between commands — use absolute paths.

- **Icons off-centre:** Avalonia does not centre a `Stretch=Uniform` Path inside its Width/Height box. Every geometry in `Icons.axaml` therefore begins with `M2 2M22 22` (move-only points count toward bounds, nothing is drawn). Keep that prefix on new icons.
