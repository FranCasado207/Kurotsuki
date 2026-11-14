using Avalonia.Controls;
using Avalonia.Interactivity;
using Kurotsuki.App.ViewModels;

namespace Kurotsuki.App.Views;

public partial class BulkDownloadWindow : Window
{
    public BulkDownloadWindow()
    {
        InitializeComponent();
        Closing += (_, _) => (DataContext as BulkDownloadViewModel)?.Cancel();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
