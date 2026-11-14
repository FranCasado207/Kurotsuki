using Avalonia.Controls;
using Avalonia.Interactivity;
using Kurotsuki.App.ViewModels;

namespace Kurotsuki.App.Views;

public partial class TransferWindow : Window
{
    public TransferWindow()
    {
        InitializeComponent();
        // Closing the window ends the transfer: nothing keeps listening on the network afterwards.
        Closed += (_, _) => (DataContext as TransferViewModel)?.Dispose();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
