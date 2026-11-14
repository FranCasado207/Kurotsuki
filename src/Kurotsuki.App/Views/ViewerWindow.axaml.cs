using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Kurotsuki.App.Services;
using Kurotsuki.App.ViewModels;
using Kurotsuki.Core.I18n;

namespace Kurotsuki.App.Views;

public partial class ViewerWindow : Window
{
    private readonly AppServices _services;

    public ViewerWindow() : this(App.Services!, null!) { }

    public ViewerWindow(AppServices services, ViewerViewModel vm)
    {
        _services = services;
        InitializeComponent();
        if (vm is null)
        {
            return;
        }
        DataContext = vm;
        vm.FitScale = () =>
        {
            var size = vm.Image?.Size;
            var view = ImageScroll.Bounds.Size;
            return size is { Width: > 0, Height: > 0 } && view.Width > 0 && view.Height > 0
                ? Math.Min(view.Width / size.Value.Width, view.Height / size.Value.Height)
                : 1;
        };
        Closed += (_, _) => vm.Dispose();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewerViewModel.Video))
            {
                // The video bitmap is updated in place, so the image must be told to redraw for every picture.
                if (vm.Video is { } video)
                {
                    video.Redraw += () => VideoImage.InvalidateVisual();
                }
            }
            if (e.PropertyName == nameof(ViewerViewModel.FitToWindow))
            {
                // Fitting needs the scroll bars off so the image is measured against the viewport; zoomed needs them on.
                var mode = vm.FitToWindow ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
                ImageScroll.HorizontalScrollBarVisibility = mode;
                ImageScroll.VerticalScrollBarVisibility = mode;
            }
        };
        ImageScroll.AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        KeyDown += OnKeyDown;
    }

    private ViewerViewModel? Vm => DataContext as ViewerViewModel;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm)
        {
            return;
        }
        switch (e.Key)
        {
            // Shift turns the arrows into video controls, so plain arrows keep paging through posts.
            case Key.Left when vm.Video is { } video && e.KeyModifiers == KeyModifiers.Shift: video.SeekBy(-10_000); break;
            case Key.Right when vm.Video is { } video && e.KeyModifiers == KeyModifiers.Shift: video.SeekBy(10_000); break;
            case Key.Up when vm.Video is { } video: video.ChangeVolume(5); break;
            case Key.Down when vm.Video is { } video: video.ChangeVolume(-5); break;
            case Key.M when vm.Video is { } video: video.ToggleMuteCommand.Execute(null); break;
            case Key.P: vm.SlideshowOn = !vm.SlideshowOn; break;
            case Key.Left: vm.PreviousCommand.Execute(null); break;
            case Key.Right: vm.NextCommand.Execute(null); break;
            case Key.Escape when WindowState == WindowState.FullScreen: WindowState = WindowState.Normal; break;
            case Key.Escape: Close(); break;
            case Key.F11: WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen; break;
            case Key.Space when vm.Video is { } video: video.TogglePauseCommand.Execute(null); break;
            case Key.OemPlus or Key.Add: vm.ZoomInCommand.Execute(null); break;
            case Key.OemMinus or Key.Subtract: vm.ZoomOutCommand.Execute(null); break;
            case Key.F: vm.FitCommand.Execute(null); break;
            case Key.D0 or Key.NumPad0: vm.ActualSizeCommand.Execute(null); break;
            default: return;
        }
        e.Handled = true;
    }

    private void ToggleFullscreen() =>
        WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;

    private void OnFullscreenClick(object? sender, RoutedEventArgs e) => ToggleFullscreen();
    private void OnVideoDoubleTapped(object? sender, TappedEventArgs e) => ToggleFullscreen();

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (Vm is { } vm && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            vm.ZoomBy(e.Delta.Y > 0 ? 1.25 : 1 / 1.25);
            e.Handled = true;
        }
    }

    private void OnFolderClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || sender is not Control target)
        {
            return;
        }
        var items = new List<Control>();
        foreach (var name in _services.Folders.Names)
        {
            var item = new MenuItem { Header = name };
            item.Click += (_, _) => vm.AddToFolder(name);
            items.Add(item);
        }
        if (items.Count > 0)
        {
            items.Add(new Separator());
        }
        var create = new MenuItem { Header = Messages.Get("folders.new") };
        create.Click += async (_, _) =>
        {
            var name = await Dialogs.PromptAsync(this, Messages.Get("folders.newTitle"), Messages.Get("folders.name"));
            if (name is not null)
            {
                _services.Folders.Create(name);
                vm.AddToFolder(name);
            }
        };
        items.Add(create);
        Menus.Show(target, items);
    }

    private void OnMoreActionsClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || sender is not Control target)
        {
            return;
        }
        var items = new List<Control>();
        if (PostActions.RelatedItem(vm.Post, _services) is { } related)
        {
            items.Add(related);
        }
        items.Add(PostActions.ReverseSearchItem(vm.Post));
        var slideshow = new MenuItem { Header = Messages.Get("next.slideshow"), ToggleType = MenuItemToggleType.CheckBox, IsChecked = vm.SlideshowOn };
        slideshow.Click += (_, _) => vm.SlideshowOn = !vm.SlideshowOn;
        items.Add(slideshow);
        Menus.Show(target, items);
    }

    private void OnPoolsClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || sender is not Control target)
        {
            return;
        }
        var items = vm.Pools.Select(pool =>
        {
            var item = new MenuItem { Header = $"{pool.DisplayName} ({pool.PostCount})" };
            item.Click += (_, _) => vm.OpenPool(pool);
            return (Control)item;
        }).ToList();
        if (items.Count == 0)
        {
            items.Add(new MenuItem { Header = Messages.Get("next.noPools"), IsEnabled = false });
        }
        Menus.Show(target, items);
    }

    private void OnTagClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: TagItemViewModel tag } && Vm is { } vm)
        {
            vm.SearchTag(tag.Tag);
        }
    }

    private void OnTagContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Control { DataContext: TagItemViewModel tag } button && Vm is { } vm)
        {
            Menus.Show(button, Menus.ForTag(tag.Tag, null, _services, vm.SearchTag,
                t => _ = PostActions.ShowTagInfoAsync(t, vm.Booru, _services, this)));
            e.Handled = true;
        }
    }
}
