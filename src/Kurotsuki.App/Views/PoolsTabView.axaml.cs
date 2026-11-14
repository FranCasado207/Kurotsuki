using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Kurotsuki.App.ViewModels;

namespace Kurotsuki.App.Views;

public partial class PoolsTabView : UserControl
{
    private const double NearBottomPixels = 500;

    public PoolsTabView()
    {
        InitializeComponent();
        AddHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged);
    }

    private async void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.Source is ScrollViewer sv && DataContext is PoolsTabViewModel tab
            && sv.Extent.Height - (sv.Offset.Y + sv.Viewport.Height) < NearBottomPixels)
        {
            await tab.LoadNextPageAsync();
        }
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as StyledElement)?.DataContext is PoolItemViewModel pool && pool.OpenCommand.CanExecute(null))
        {
            pool.OpenCommand.Execute(null);
        }
    }
}
