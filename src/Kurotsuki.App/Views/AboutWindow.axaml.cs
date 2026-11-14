using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Kurotsuki.Core.I18n;

namespace Kurotsuki.App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var version = typeof(AboutWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "dev";
        VersionText.Text = Messages.Get("about.version", version);
        AuthorText.Text = Messages.Get("about.author", "FranCasado207");
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
