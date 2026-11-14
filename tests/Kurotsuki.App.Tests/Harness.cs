using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Kurotsuki.App.Services;
using Kurotsuki.Core.Config;

namespace Kurotsuki.App.Tests;

/// <summary>Runs the real application classes headless, on a private data directory and a stubbed booru.</summary>
public static class Harness
{
    private static HeadlessUnitTestSession? _session;
    private static readonly object Gate = new();
    public static string Root { get; } = Path.Combine(Path.GetTempPath(), "kt-ui-" + Guid.NewGuid().ToString("N"));
    public static StubBooru Stub { get; } = new();

    [ModuleInitializer]
    internal static void Init()
    {
        // Must run before anything reads AppPaths: the app would otherwise use the real ~/.config.
        Directory.CreateDirectory(Path.Combine(Root, "cfg", "kurotsuki"));
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(Root, "cfg"));
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(Root, "data"));
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", Path.Combine(Root, "cache"));
        Environment.SetEnvironmentVariable("KT_VIDEO_NO_AUDIO", "1");
        ThreadPool.SetMinThreads(64, 64);
        File.WriteAllText(Path.Combine(Root, "cfg", "kurotsuki", "config.json"), $$"""
            {
              "booruConfigurations": [
                { "engine": "DANBOORU_SOFTWARE", "customBaseUrl": "{{StubBooru.Host}}" }
              ],
              "downloadFolder": "{{Path.Combine(Root, "downloads").Replace("\\", "/")}}",
              "blacklistedTags": ["blocked_tag"],
              "language": "en"
            }
            """);
    }

    public sealed class TestApp : App
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }

    public static Task<T> Run<T>(Func<Task<T>> work)
    {
        lock (Gate)
        {
            _session ??= HeadlessUnitTestSession.StartNew(typeof(TestApp));
        }
        return _session.Dispatch(work, CancellationToken.None);
    }

    public static Task Run(Func<Task> work) => Run(async () => { await work(); return 0; });

    public static AppServices NewServices() => AppServices.Create(
        new HttpClient(Stub),
        new HttpClient(Stub));

    /// <summary>Like <see cref="NewServices"/> but with a private folder for the session and next-settings files.</summary>
    public static AppServices NewIsolatedServices() => AppServices.Create(
        new HttpClient(Stub),
        new HttpClient(Stub),
        Directory.CreateTempSubdirectory("kt-state").FullName);

    public static async Task WaitFor(Func<bool> condition, int timeoutMs = 20000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("Condition not met in time");
            }
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Saves the window's last rendered frame when KT_SCREENSHOTS points at a folder (for looking at the UI by hand).</summary>
    public static void Capture(Avalonia.Controls.Window window, string name)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatformExtensions_Capture(window, name);
    }

    private static void AvaloniaHeadlessPlatformExtensions_Capture(Avalonia.Controls.Window window, string name)
    {
        var frame = HeadlessWindowExtensions.CaptureRenderedFrame(window);
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("KT_SCREENSHOTS") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            using var file = File.Create(Path.Combine(dir, name + ".png"));
#pragma warning disable CS0618 // the replacement overload needs encoder options we do not care about
            frame!.Save(file);
#pragma warning restore CS0618
        }
    }
}
