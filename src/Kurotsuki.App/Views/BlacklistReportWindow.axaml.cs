using Avalonia.Controls;
using Avalonia.Interactivity;
using Kurotsuki.App.ViewModels;

namespace Kurotsuki.App.Views;

public partial class BlacklistReportWindow : Window
{
    public BlacklistReportWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as BlacklistReportViewModel)?.Dispose();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
