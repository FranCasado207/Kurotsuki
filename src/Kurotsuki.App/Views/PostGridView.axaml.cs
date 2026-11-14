using Kurotsuki.Core.I18n;
using Avalonia.VisualTree;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Kurotsuki.App.Services;
using Kurotsuki.App.ViewModels;

namespace Kurotsuki.App.Views;

public partial class PostGridView : UserControl
{
    /// <summary>Load the next page when fewer than this many pixels remain below the viewport.</summary>
    private const double NearBottomPixels = 600;

    public PostGridView()
    {
        InitializeComponent();
        AddHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged);
        DataContextChanged += (_, _) =>
        {
            if (Grid_ is { } grid)
            {
                grid.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(PostGridViewModel.FilterVisible) && grid.FilterVisible)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => FilterBox.Focus());
                    }
                };
            }
        };
        FilterBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && Grid_ is { } g)
            {
                g.FilterVisible = false;
                e.Handled = true;
            }
        };
        // Ctrl + wheel resizes the thumbnails; tunnelled so the list does not scroll as well.
        AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            if (e.KeyModifiers == KeyModifiers.Control && Grid_ is { } grid)
            {
                grid.TileSize = Math.Clamp(grid.TileSize + Math.Sign(e.Delta.Y) * 20, PostGridViewModel.MinTile, PostGridViewModel.MaxTile);
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private PostGridViewModel? Grid_ => DataContext as PostGridViewModel;

    private async void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.Source is not ScrollViewer sv || Grid_ is not { } grid || sv.Viewport.Height <= 0)
        {
            return; // a hidden tab reports an empty viewport; that is not the user scrolling
        }
        var max = sv.Extent.Height - sv.Viewport.Height;
        if (grid.PendingScroll > 0)
        {
            // Restoring a saved position: load pages until the grid is tall enough to scroll there.
            if (max >= grid.PendingScroll || !grid.HasMore)
            {
                sv.Offset = new Vector(sv.Offset.X, Math.Min(grid.PendingScroll, Math.Max(0, max)));
                grid.PendingScroll = 0;
            }
            else
            {
                await grid.LoadNextPageAsync();
            }
            return;
        }
        grid.ScrollOffset = sv.Offset.Y;
        if (max - sv.Offset.Y < NearBottomPixels)
        {
            await grid.LoadNextPageAsync();
        }
    }

    private void OnSortClick(object? sender, RoutedEventArgs e)
    {
        if (Grid_ is not { } grid || sender is not Control button)
        {
            return;
        }
        MenuItem Choice(string key, PostSort sort)
        {
            var item = new MenuItem { Header = Messages.Get(key), ToggleType = MenuItemToggleType.Radio, IsChecked = grid.Sort == sort };
            item.Click += (_, _) => grid.Sort = sort;
            return item;
        }
        Menus.Show(button,
        [
            Choice("next.sortLoadOrder", PostSort.LoadOrder),
            Choice("next.sortScore", PostSort.Score),
            Choice("next.sortNewest", PostSort.Newest),
            Choice("next.sortResolution", PostSort.Resolution),
        ]);
    }

    private void OnQueueSelected(object? sender, RoutedEventArgs e) =>
        Grid_?.AddToQueue(Grid.SelectedItems?.OfType<PostViewModel>().Where(p => p.Shown).ToList() ?? []);

    private static PostViewModel? PostOf(object? sender) => (sender as Control)?.DataContext as PostViewModel;

    private void OnTileQueueClick(object? sender, RoutedEventArgs e) => PostOf(sender)?.ToggleQueue();
    private void OnTileFavoriteClick(object? sender, RoutedEventArgs e) => PostOf(sender)?.ToggleFavorite();

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual source && source.GetVisualAncestors().OfType<Button>().Any())
        {
            return;
        }
        // The tile under the pointer, not the selection: the grid selects with Toggle, so the second click of a
        // double-click can deselect the tile and leave a different one selected.
        if ((e.Source as StyledElement)?.DataContext is PostViewModel post)
        {
            Grid_?.OpenViewer(post);
        }
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Grid.SelectedItem is PostViewModel post)
        {
            Grid_?.OpenViewer(post);
            e.Handled = true;
        }
        else if (e.Key == Key.A && e.KeyModifiers == KeyModifiers.Control)
        {
            Grid.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Grid.UnselectAll();
        }
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Control { DataContext: PostViewModel post } tile && Grid_ is { } grid)
        {
            // Right-clicking a tile that is not part of the selection acts on that tile only.
            if (!Grid.SelectedItems!.Contains(post))
            {
                Grid.SelectedItems.Clear();
                Grid.SelectedItems.Add(post);
            }
            Menus.Show(tile, Menus.ForPost(post, grid.Services));
            e.Handled = true;
        }
    }
}
