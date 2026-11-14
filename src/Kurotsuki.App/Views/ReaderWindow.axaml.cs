using Kurotsuki.Core.I18n;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kurotsuki.App.Services;
using Kurotsuki.App.ViewModels;

namespace Kurotsuki.App.Views;

public partial class ReaderWindow : Window
{
    private readonly Dictionary<Border, ReaderPageViewModel> _realized = [];
    private Point? _dragFrom;
    private Vector _dragOffset;

    public ReaderWindow() : this(App.Services!, null!) { }

    public ReaderWindow(AppServices services, ReaderViewModel vm)
    {
        InitializeComponent();
        if (vm is null)
        {
            return;
        }
        DataContext = vm;
        Closed += (_, _) => vm.Dispose();
        vm.Shown.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(RebuildPaged);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ReaderViewModel.Fit) or nameof(ReaderViewModel.Zoom) or nameof(ReaderViewModel.Mode))
            {
                Dispatcher.UIThread.Post(Relayout);
            }
        };
        ViewArea.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty)
            {
                Relayout();
            }
        };
        PagedScroll.AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        StripScroll.AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        PagedScroll.PointerPressed += OnDragStart;
        PagedScroll.PointerMoved += OnDragMove;
        PagedScroll.PointerReleased += (_, _) => _dragFrom = null;
        StripScroll.ScrollChanged += (_, _) => UpdateStripPosition();
        KeyDown += OnKeyDown;
        // The pool may already be loaded (fast network, cached responses): show what is there now.
        RebuildPaged();
        Opened += (_, _) => Relayout();
    }

    private void OnBookmarksClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || sender is not Control button)
        {
            return;
        }
        var items = vm.Bookmarks.Select(page =>
        {
            var item = new MenuItem { Header = Messages.Get("reader.page", page + 1, vm.Pages.Count) };
            item.Click += (_, _) => vm.GoToPage(page);
            return (Control)item;
        });
        Menus.Show(button, items);
    }

    private void OnNextPoolClick(object? sender, RoutedEventArgs e)
    {
        Vm?.OpenNextPool();
        Close();
    }

    private ReaderViewModel? Vm => DataContext as ReaderViewModel;

    // ---- paged layout ---------------------------------------------------------------------------------------

    private void RebuildPaged()
    {
        if (Vm is not { } vm)
        {
            return;
        }
        PagedHost.Children.Clear();
        foreach (var page in vm.Shown)
        {
            var image = new Image { Stretch = Stretch.Uniform, DataContext = page };
            image.Bind(Image.SourceProperty, new Binding(nameof(ReaderPageViewModel.Bitmap)));
            var label = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, DataContext = page };
            label.Bind(TextBlock.TextProperty, new Binding(nameof(ReaderPageViewModel.Error)));
            var spinner = new ProgressBar { IsIndeterminate = true, Width = 120, DataContext = page };
            spinner.Bind(IsVisibleProperty, new Binding(nameof(ReaderPageViewModel.IsLoading)));
            var host = new Panel { Tag = page, Children = { image, label, spinner } };
            PagedHost.Children.Add(host);
            // A page that finishes loading (or is found to be a different shape) changes the layout.
            page.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ReaderPageViewModel.Bitmap))
                {
                    Dispatcher.UIThread.Post(Relayout);
                }
            };
        }
        PagedScroll.Offset = default;
        Relayout();
    }

    private void Relayout()
    {
        if (Vm is not { } vm)
        {
            return;
        }
        var viewport = PagedScroll.Bounds.Size;
        if (viewport.Width < 1 || viewport.Height < 1)
        {
            viewport = ViewArea.Bounds.Size;
        }
        if (vm.IsScroll)
        {
            // Zoom widens the strip; the scroll bar appears when it is wider than the window.
            vm.StripWidth = Math.Max(100, (ViewArea.Bounds.Width - 20) * vm.Zoom);
            return;
        }
        var count = Math.Max(1, PagedHost.Children.Count);
        var width = viewport.Width / count * vm.Zoom;
        var height = viewport.Height * vm.Zoom;
        foreach (var child in PagedHost.Children.OfType<Panel>())
        {
            var page = (ReaderPageViewModel)child.Tag!;
            var aspect = page.Aspect > 0 ? page.Aspect : 0.7;
            if (vm.Fit == ReaderFit.Width)
            {
                child.Width = width;
                child.Height = width / aspect;
            }
            else
            {
                // Largest size with the page's aspect ratio that fits the slot.
                var w = Math.Min(width, height * aspect);
                child.Width = w;
                child.Height = w / aspect;
            }
        }
        PagedScroll.HorizontalScrollBarVisibility = vm.Zoom > 1.001 ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
    }

    // ---- scroll mode ----------------------------------------------------------------------------------------

    private void OnStripItemAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Border { DataContext: ReaderPageViewModel page } border && Vm is { } vm)
        {
            _realized[border] = page;
            vm.SetVisiblePages(_realized.Values.Select(p => p.Number));
        }
    }

    private void OnStripItemDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Border border && _realized.Remove(border) && Vm is { } vm)
        {
            vm.SetVisiblePages(_realized.Values.Select(p => p.Number));
        }
    }

    /// <summary>The page under the middle of the viewport counts as the current one (for the page counter and progress).</summary>
    private void UpdateStripPosition()
    {
        if (Vm is not { IsScroll: true } vm)
        {
            return;
        }
        var middle = StripScroll.Bounds.Height / 2;
        foreach (var (border, page) in _realized)
        {
            if (border.TranslatePoint(new Point(0, 0), StripScroll) is { } top
                && top.Y <= middle && top.Y + border.Bounds.Height >= middle)
            {
                vm.ScrolledTo(page.Number);
                return;
            }
        }
    }

    // ---- input ----------------------------------------------------------------------------------------------

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm)
        {
            return;
        }
        // In right-to-left reading the arrows are mirrored: "forward" is to the left.
        var forwardKey = vm.RightToLeft ? Key.Left : Key.Right;
        var backKey = vm.RightToLeft ? Key.Right : Key.Left;
        if (e.Key == forwardKey || e.Key is Key.PageDown or Key.D && !vm.RightToLeft || e.Key == Key.A && vm.RightToLeft)
        {
            vm.Step(1);
        }
        else if (e.Key == backKey || e.Key is Key.PageUp or Key.A && !vm.RightToLeft || e.Key == Key.D && vm.RightToLeft)
        {
            vm.Step(-1);
        }
        else if (e.Key == Key.Space)
        {
            vm.Step(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
        }
        else if (e.Key == Key.Home) vm.GoToPage(0);
        else if (e.Key == Key.End) vm.GoToPage(vm.Pages.Count - 1);
        else if (e.Key is Key.OemPlus or Key.Add) vm.ZoomInCommand.Execute(null);
        else if (e.Key is Key.OemMinus or Key.Subtract) vm.ZoomOutCommand.Execute(null);
        else if (e.Key is Key.D0 or Key.NumPad0) vm.ResetZoomCommand.Execute(null);
        else if (e.Key == Key.F) vm.ToggleFitCommand.Execute(null);
        else if (e.Key == Key.S) vm.ToggleSpreadCommand.Execute(null);
        else if (e.Key == Key.M) vm.ToggleModeCommand.Execute(null);
        else if (e.Key == Key.B) vm.ToggleBookmarkCommand.Execute(null);
        else if (e.Key == Key.Escape) Close();
        else return;
        e.Handled = true;
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (Vm is not { } vm)
        {
            return;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            vm.ZoomBy(e.Delta.Y > 0 ? 1.25 : 1 / 1.25);
            e.Handled = true;
        }
        else if (vm.IsPaged && sender is ScrollViewer sv)
        {
            // At the end of the page, the wheel turns it (as in the JavaFX reader).
            var atBottom = sv.Offset.Y >= sv.Extent.Height - sv.Viewport.Height - 1;
            var atTop = sv.Offset.Y <= 1;
            if (e.Delta.Y < 0 && atBottom)
            {
                vm.Step(1);
                e.Handled = true;
            }
            else if (e.Delta.Y > 0 && atTop)
            {
                vm.Step(-1);
                e.Handled = true;
            }
        }
    }

    private void OnDragStart(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(PagedScroll).Properties.IsLeftButtonPressed && Vm is { Zoom: > 1.001 } or { Fit: ReaderFit.Width })
        {
            _dragFrom = e.GetPosition(PagedScroll);
            _dragOffset = PagedScroll.Offset;
        }
    }

    private void OnDragMove(object? sender, PointerEventArgs e)
    {
        if (_dragFrom is { } from)
        {
            var now = e.GetPosition(PagedScroll);
            PagedScroll.Offset = new Vector(_dragOffset.X - (now.X - from.X), _dragOffset.Y - (now.Y - from.Y));
        }
    }
}
