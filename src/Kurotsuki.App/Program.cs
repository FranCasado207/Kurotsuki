using Avalonia;

namespace Kurotsuki.App;

internal static class Program
{
    // Don't use Avalonia, third-party APIs or any SynchronizationContext-reliant code before AppMain is called.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
#if DEBUG
        .WithDeveloperTools()
#endif
        .WithInterFont()
        .LogToTrace();
}
