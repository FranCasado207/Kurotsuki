using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Kurotsuki.App.Services;
using Kurotsuki.App.ViewModels;
using Kurotsuki.App.Views;
using Kurotsuki.Core.Logging;

namespace Kurotsuki.App;

public partial class App : Application
{
    /// <summary>The shared services; views build code-made menus from it.</summary>
    public static AppServices? Services { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = AppServices.Create();
            AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLog.Error("Unhandled exception", e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, e) => AppLog.Warning("Unobserved task exception", e.Exception);
            Services = services;
            var window = new MainWindow { DataContext = new MainViewModel(services) };
            desktop.MainWindow = window;
            services.Theme.WatchSystem();
            var tray = new DownloadTray(services);
            TrayIcon.SetIcons(this, [tray.Icon]);
            desktop.Exit += (_, _) =>
            {
                (window.DataContext as MainViewModel)?.SaveSession();
                tray.Dispose();
                services.Dispose();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
