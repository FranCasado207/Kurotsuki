using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Kurotsuki.App.Services;
using Avalonia.Threading;
using Kurotsuki.App.ViewModels;
using Kurotsuki.App.Views;
using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Download;

namespace Kurotsuki.App.Tests;

public class UiTests
{
    private static async Task<(MainWindow Window, MainViewModel Vm)> OpenMain()
    {
        var services = Harness.NewServices();
        var vm = new MainViewModel(services);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();
        await Task.Yield();
        return (window, vm);
    }

    [Fact]
    public Task SearchShowsTilesWithThumbnailsAndBlacklistHidesPosts() => Harness.Run(async () =>
    {
        var (window, vm) = await OpenMain();
        Assert.Single(vm.Boorus);
        vm.SearchText = "cat";
        vm.SearchCommand.Execute(null);
        var tab = Assert.IsType<SearchTabViewModel>(vm.SelectedTab);
        await Harness.WaitFor(() => tab.Posts.Count == 7 && tab.Posts.All(p => p.Thumbnail is not null));

        Assert.False(tab.ShowEmpty);
        Assert.All(tab.Posts, p => Assert.False(p.ThumbnailFailed));
        Harness.Capture(window, "main-search");
        window.Close();
    });

    [Fact]
    public Task QueueAndDownloadWritesFilesAndRemembersHistory() => Harness.Run(async () =>
    {
        var (window, vm) = await OpenMain();
        vm.SearchCommand.Execute(null);
        var tab = Assert.IsType<SearchTabViewModel>(vm.SelectedTab);
        await Harness.WaitFor(() => tab.Posts.Count == 7);

        tab.AddToQueue(tab.Posts.Take(2));
        Assert.Equal(2, vm.QueueItems.Count);
        vm.StartAllCommand.Execute(null);
        await Harness.WaitFor(() => vm.QueueItems.All(i => i.Status == DownloadStatus.Done), 15000);

        Assert.All(vm.QueueItems, i => Assert.True(File.Exists(i.SavedPath)));
        Assert.All(tab.Posts.Take(2), p => Assert.True(p.WasDownloaded));
        Harness.Capture(window, "main-queue-done");
        window.Close();
    });

    [Fact]
    public Task ViewerLoadsImageAndTagsWithCategories() => Harness.Run(async () =>
    {
        var services = Harness.NewServices();
        var vm = new MainViewModel(services);
        var main = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        main.Show();
        vm.SearchCommand.Execute(null);
        var tab = Assert.IsType<SearchTabViewModel>(vm.SelectedTab);
        await Harness.WaitFor(() => tab.Posts.Count == 7);

        tab.OpenViewer(tab.Posts[1]);
        var viewer = vm.Viewer!;
        var viewerVm = (ViewerViewModel)viewer.DataContext!;
        await Harness.WaitFor(() => viewerVm.Image is not null);

        Assert.Equal(2, viewerVm.Post.Id);
        Assert.Contains(viewerVm.Tags, t => t.Tag == "artist_x" && t.Category == TagCategory.Artist);
        Assert.True(viewerVm.CanGoForward);
        viewerVm.NextCommand.Execute(null);
        await Harness.WaitFor(() => viewerVm.Post.Id == 3 && viewerVm.Image is not null);
        viewerVm.ToggleFavoriteCommand.Execute(null);
        Assert.True(viewerVm.IsFavorite);
        Harness.Capture(viewer, "viewer");
        viewer.Close();
        main.Close();
    });

    [Fact]
    public Task FavoritesFoldersAndTagGroupsPersistInTheLibrary() => Harness.Run(async () =>
    {
        var services = Harness.NewServices();
        var post = new Post { Id = 55, Image = "a.png", Tags = "x y", BooruBaseUrl = StubBooru.Host, FileUrl = StubBooru.Host + "/media/a.png" };

        services.Favorites.TogglePost(post);
        services.Favorites.ToggleTag("cat_ears");
        var group = services.TagGroups.Create("Cats");
        services.TagGroups.AddTag(group, "cat_ears");
        services.Folders.Create("Keep");
        services.Folders.Add("Keep", post);
        services.Dispose();

        var again = Harness.NewServices();
        Assert.True(again.Favorites.IsPostFavorite(post));
        Assert.Contains("cat_ears", again.Favorites.Tags);
        Assert.Equal("Cats", again.TagGroups.GroupFor(new Post { Tags = "cat_ears" }));
        Assert.Single(again.Folders.PostsOf("Keep"));
        again.Dispose();
        await Task.CompletedTask;
    });

    [Fact]
    public Task PoolsTabListsPoolsAndReaderShowsPages() => Harness.Run(async () =>
    {
        var services = Harness.NewServices();
        var vm = new MainViewModel(services);
        var main = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        main.Show();
        vm.OpenPools();
        var pools = Assert.IsType<PoolsTabViewModel>(vm.SelectedTab);
        await Harness.WaitFor(() => pools.Pools.Count == 1);
        Assert.Equal("Stub comic", pools.Pools[0].Name);
        await Harness.WaitFor(() => pools.Pools[0].Cover is not null);
        Harness.Capture(main, "pools");

        pools.Pools[0].ReadCommand.Execute(null);
        var reader = vm.LastReader!;
        var readerVm = (ReaderViewModel)reader.DataContext!;
        await Harness.WaitFor(() => readerVm.Pages.Count == StubBooru.PoolPages && readerVm.Shown.Count == 1
            && readerVm.Shown[0].Bitmap is not null);
        reader.Show();
        Harness.Capture(reader, "reader");
        var host = reader.FindControl<StackPanel>("PagedHost")!;
        Assert.Single(host.Children);
        Assert.True(((Control)host.Children[0]).Bounds.Width > 100);

        readerVm.Step(1);
        readerVm.Step(1);
        readerVm.Step(1);
        await Harness.WaitFor(() => services.Reading.IsFinished(readerVm.Pool.Key));
        Assert.Equal(StubBooru.PoolPages - 1, services.Reading.PageOf(readerVm.Pool.Key));

        readerVm.ToggleSpreadCommand.Execute(null);
        Assert.True(readerVm.Spread);
        readerVm.ToggleModeCommand.Execute(null);
        Assert.True(readerVm.IsScroll);
        reader.Close();
        main.Close();
    });

    [Fact]
    public Task PoolsToggleOnlyShowsForBoorusWithPools() => Harness.Run(async () =>
    {
        var (window, vm) = await OpenMain();
        var custom = new Kurotsuki.Core.Config.BooruConfiguration
        {
            Engine = Kurotsuki.Core.Booru.BooruEngine.GelbooruFamily, CustomBaseUrl = "https://example.org",
        };
        vm.SelectedBooru = custom;
        Assert.False(vm.CanPools);
        Assert.Equal("example.org", custom.ShortName);
        vm.SelectedBooru = vm.Boorus[0];
        Assert.True(vm.CanPools);
        vm.PoolMode = true;
        vm.SelectedBooru = custom;
        Assert.False(vm.PoolMode);
        window.Close();
    });

    [Fact]
    public Task AddingABooruNeedsASuccessfulTest() => Harness.Run(async () =>
    {
        var services = Harness.NewServices();
        var vm = new SettingsViewModel(services);
        var before = vm.Boorus.Count;
        vm.NewKind = BooruKind.All.First(k => k.Engine == Kurotsuki.Core.Booru.BooruEngine.DanbooruSoftware);
        vm.NewBaseUrl = StubBooru.Host;

        Assert.False(vm.AddBooruCommand.CanExecute(null));
        await vm.TestBooruCommand.ExecuteAsync(null);
        Assert.True(vm.Tested, vm.Error);
        Assert.True(vm.AddBooruCommand.CanExecute(null));

        vm.NewApiKey = "changed";
        Assert.False(vm.Tested);
        Assert.False(vm.AddBooruCommand.CanExecute(null));

        vm.NewApiKey = "";
        await vm.TestBooruCommand.ExecuteAsync(null);
        vm.AddBooruCommand.Execute(null);
        Assert.Equal(before + 1, vm.Boorus.Count);
        Assert.False(vm.Tested);

        // The stub answers every host with Danbooru JSON, which a Szurubooru client cannot read.
        vm.NewKind = BooruKind.All.First(k => k.Engine == Kurotsuki.Core.Booru.BooruEngine.Szurubooru);
        await vm.TestBooruCommand.ExecuteAsync(null);
        Assert.False(vm.Tested);
        Assert.NotEmpty(vm.Error);
    });

    [Fact]
    public Task SettingsWindowOpensAndSavesBackToTheSameConfig() => Harness.Run(async () =>
    {
        var services = Harness.NewServices();
        var vm = new SettingsViewModel(services) { DownloadTemplate = "%id%.%ext%" };
        var window = new SettingsWindow { DataContext = vm };
        window.Show();
        Harness.Capture(window, "settings");
        vm.Save();
        Assert.Equal("%id%.%ext%", services.ConfigManager.Load().DownloadTemplate);
        window.Close();
        await Task.CompletedTask;
    });
}

public class ContextMenuTests
{
    [Fact]
    public Task RightClickOnATileOpensTheMenuWithPostActions() => Harness.Run(async () =>
    {
        var services = Harness.NewServices();
        var vm = new MainViewModel(services);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();
        vm.SearchCommand.Execute(null);
        var tab = Assert.IsType<SearchTabViewModel>(vm.SelectedTab);
        await Harness.WaitFor(() => tab.Posts.Count == 7);
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var tile = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window)
            .OfType<Border>().First(b => b.Classes.Contains("tile") && b.DataContext is PostViewModel);
        var at = tile.TranslatePoint(new Avalonia.Point(tile.Bounds.Width / 2, tile.Bounds.Height / 2), window)!.Value;
        Menus.ClearLast();
        Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window, at, Avalonia.Input.MouseButton.Right);
        Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window, at, Avalonia.Input.MouseButton.Right);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var menu = Menus.LastOpened;
        Assert.NotNull(menu);
        Assert.True(menu!.IsOpen);
        var headers = menu.Items.OfType<MenuItem>().Select(i => i.Header?.ToString()).ToList();
        Assert.Contains("Add to queue", headers);
        Assert.Contains("Favorite", headers);
        Assert.Contains("Add to folder", headers);
        menu.Close();
        window.Close();
    });
}

public class ThemeUiTests
{
    [Fact]
    public Task SwitchingThemesRecolorsTheBrushesAndSettingsCanSaveACustomTheme() => Harness.Run(async () =>
    {
        var services = Harness.NewServices();
        var vm = new MainViewModel(services);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();
        vm.SearchCommand.Execute(null);
        var tab = Assert.IsType<SearchTabViewModel>(vm.SelectedTab);
        await Harness.WaitFor(() => tab.Posts.Count == 7 && tab.Posts.All(p => p.Thumbnail is not null));

        static Avalonia.Media.Color BrushColor(string key) =>
            ((Avalonia.Media.SolidColorBrush)Avalonia.Application.Current!.FindResource(key)!).Color;

        foreach (var preset in Kurotsuki.Core.Theming.ThemeDefinition.BuiltIns)
        {
            services.Theme.Apply(preset);
            window.UpdateLayout();
            Harness.Capture(window, "theme-" + preset.Name.ToLowerInvariant());
        }
        services.Theme.Apply(Kurotsuki.Core.Theming.ThemeDefinition.Light);
        Assert.Equal(Avalonia.Media.Color.Parse("#F5F3EF"), BrushColor("KtBackgroundBrush"));
        Assert.Equal(Avalonia.Styling.ThemeVariant.Light, Avalonia.Application.Current!.RequestedThemeVariant);
        window.UpdateLayout();
        Harness.Capture(window, "main-light");

        var settings = new SettingsViewModel(services);
        var editor = settings.Appearance;
        editor.NewCommand.Execute(null);
        editor.Name = "Sunset";
        editor.Tokens.First(t => t.Token == "accent").Hex = "#FF7A59";
        Assert.Equal(Avalonia.Media.Color.Parse("#FF7A59"), BrushColor("KtAccentBrush"));
        editor.SaveThemeCommand.Execute(null);
        Assert.Equal("Sunset", editor.Selected?.Name);
        settings.Save();
        Assert.Equal("Sunset", new Kurotsuki.Core.Theming.ThemeStore(services.Paths.Config).SelectedName());
        Harness.Capture(window, "main-custom");

        // Cancelling restores what was active when the editor opened.
        var again = new SettingsViewModel(services);
        again.Appearance.Selected = again.Appearance.Themes.First(t => t.Name == "Dark");
        Assert.Equal(Avalonia.Media.Color.Parse("#0B0B10"), BrushColor("KtBackgroundBrush"));
        again.Appearance.Revert();
        Assert.Equal(Avalonia.Media.Color.Parse("#FF7A59"), BrushColor("KtAccentBrush"));

        var settingsWindow = new SettingsWindow { DataContext = new SettingsViewModel(services) };
        settingsWindow.Show();
        ((SettingsViewModel)settingsWindow.DataContext).Appearance.Selected = Kurotsuki.Core.Theming.ThemeDefinition.Dark;
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(settingsWindow).OfType<TabControl>().First().SelectedIndex = 1;
        settingsWindow.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Harness.Capture(settingsWindow, "settings-appearance");
        settingsWindow.Close();
        window.Close();
        services.Theme.Store.Delete("Sunset");
    });
}

public class DoubleClickTests
{
    [Fact]
    public Task DoubleClickOpensTheTileUnderThePointerEvenWhenAnotherIsSelected() => Harness.Run(async () =>
    {
        var services = Harness.NewServices();
        var vm = new MainViewModel(services);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();
        vm.SearchCommand.Execute(null);
        var tab = Assert.IsType<SearchTabViewModel>(vm.SelectedTab);
        await Harness.WaitFor(() => tab.Posts.Count == 7);
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Avalonia.Point Center(PostViewModel post)
        {
            var tile = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window)
                .OfType<Border>().First(b => b.Classes.Contains("tile") && ReferenceEquals(b.DataContext, post));
            return tile.TranslatePoint(new Avalonia.Point(tile.Bounds.Width / 2, tile.Bounds.Height / 2), window)!.Value;
        }
        void Click(Avalonia.Point at)
        {
            Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window, at, Avalonia.Input.MouseButton.Left);
            Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window, at, Avalonia.Input.MouseButton.Left);
        }

        Click(Center(tab.Posts[0]));
        var target = tab.Posts[4];
        Click(Center(target));
        Click(Center(target));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var viewer = Assert.IsType<ViewerWindow>(vm.Viewer);
        Assert.Equal(target.Post.Id, ((ViewerViewModel)viewer.DataContext!).Post.Id);
        viewer.Close();
        window.Close();
    });
}

public class PolishTests
{
    [Fact]
    public Task TileSizeResizesTilesIsSharedAndRemembered() => Harness.Run(async () =>
    {
        var services = Harness.NewServices();
        var vm = new MainViewModel(services);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();
        vm.SearchCommand.Execute(null);
        var tab = Assert.IsType<SearchTabViewModel>(vm.SelectedTab);
        await Harness.WaitFor(() => tab.Posts.Count == 7);

        tab.TileSize = 300;
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var tile = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window)
            .OfType<Border>().First(b => b.Classes.Contains("tile") && b.DataContext is PostViewModel);
        Assert.Equal(300, tile.Bounds.Width);
        Assert.Equal(300, services.Layout.Get("grid.tile", 0));

        // Hover state: caption, quick actions and the eased-in picture.
        var hoverAt = tile.TranslatePoint(new Avalonia.Point(tile.Bounds.Width / 2, tile.Bounds.Height / 2), window)!.Value;
        Avalonia.Headless.HeadlessWindowExtensions.MouseMove(window, hoverAt);
        await Task.Delay(400);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Harness.Capture(window, "tile-hover");
        Avalonia.Controls.ToolTip.SetIsOpen(tile, true);
        await Task.Delay(600);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Harness.Capture(window, "tile-tooltip");
        Avalonia.Controls.ToolTip.SetIsOpen(tile, false);
        Avalonia.Headless.HeadlessWindowExtensions.MouseMove(window, new Avalonia.Point(5, 5));

        // The pill's Pools chip sends the same query to the pools browser instead of a post search.
        vm.PoolMode = true;
        vm.SearchText = "series";
        vm.SearchCommand.Execute(null);
        var pools = Assert.IsType<PoolsTabViewModel>(vm.SelectedTab);
        Assert.Equal("series", pools.Query);
        vm.PoolMode = false;
        vm.SelectedTab = tab;

        // Tags drawer open: the icon buttons there must be centred and the plus symmetric.
        services.Favorites.ToggleTag("cat_ears");
        vm.SidebarVisible = true;
        Harness.Capture(window, "main-tags-drawer");

        // A grid opened later starts at the remembered size.
        vm.OpenPools();
        Assert.Equal(300, new FavoritesTabViewModel(services).TileSize);
        window.Close();
    });

    [Fact]
    public Task QueuedBadgeDisappearsOnceTheDownloadIsSaved() => Harness.Run(async () =>
    {
        var services = Harness.NewServices();
        var vm = new MainViewModel(services);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();
        vm.SearchCommand.Execute(null);
        var tab = Assert.IsType<SearchTabViewModel>(vm.SelectedTab);
        await Harness.WaitFor(() => tab.Posts.Count == 7);
        var post = tab.Posts[3];
        tab.AddToQueue([post]);
        Assert.True(post.ShowQueued);
        vm.StartAllCommand.Execute(null);
        await Harness.WaitFor(() => post.WasDownloaded, 15000);
        Assert.False(post.ShowQueued);
        window.Close();
    });
}

public class ZoomAndHistoryTests
{
    [Fact]
    public Task ZoomActuallyScalesTheImageStartingFromTheFittedSize() => Harness.Run(async () =>
    {
        var services = Harness.NewServices();
        var vm = new MainViewModel(services);
        var main = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        main.Show();
        vm.SearchCommand.Execute(null);
        var tab = Assert.IsType<SearchTabViewModel>(vm.SelectedTab);
        await Harness.WaitFor(() => tab.Posts.Count == 7);
        tab.OpenViewer(tab.Posts[1]);
        var viewer = vm.Viewer!;
        var viewerVm = (ViewerViewModel)viewer.DataContext!;
        await Harness.WaitFor(() => viewerVm.Image is not null);
        viewer.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Image Picture() => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(viewer.FindControl<ScrollViewer>("ImageScroll")!)
            .OfType<Image>().First();
        var fitted = Picture().Bounds.Width;
        Assert.True(fitted > 50);

        viewerVm.ZoomInCommand.Execute(null);
        viewer.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(Avalonia.Media.Stretch.Fill, viewerVm.ImageStretch);
        Assert.Equal(fitted * 1.25, Picture().Bounds.Width, tolerance: 3);

        viewerVm.ZoomInCommand.Execute(null);
        viewer.UpdateLayout();
        Assert.Equal(fitted * 1.25 * 1.25, Picture().Bounds.Width, tolerance: 3);
        Assert.Equal(viewerVm.Image!.Size.Width * viewerVm.Zoom, Picture().Bounds.Width, tolerance: 3);
        viewer.Close();
        main.Close();
    });

    [Fact]
    public Task SearchesAreRecordedInTheHistory() => Harness.Run(async () =>
    {
        var services = Harness.NewServices();
        var vm = new MainViewModel(services) { SearchText = "history_probe tag" };
        vm.SearchCommand.Execute(null);
        Assert.Equal("history_probe tag", services.History.Items[0]);
        // Let the page and its thumbnails finish: loads left running would write the shared cache folder while other tests use it.
        var tab = Assert.IsType<SearchTabViewModel>(vm.SelectedTab);
        await Harness.WaitFor(() => tab.Posts.Count == 7 && tab.Posts.All(p => p.Thumbnail is not null || p.ThumbnailFailed));
    });
}

public class BlacklistReportUiTests
{
    [Fact]
    public Task ReportListsHiddenPostsAndShowAnywayKeepsThemVisibleInLaterSearches() => Harness.Run(async () =>
    {
        var services = Harness.NewServices();
        services.Config.BlacklistedTags.Add("cat_ears"); // posts 2, 4 and 6 of the stub carry it
        var vm = new MainViewModel(services);
        var main = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        main.Show();
        vm.SearchCommand.Execute(null);
        var tab = Assert.IsType<SearchTabViewModel>(vm.SelectedTab);
        await Harness.WaitFor(() => tab.Posts.Count == 4 && tab.Posts.All(p => p.Thumbnail is not null));
        Assert.Equal(3, tab.Hidden.Count);
        Assert.True(tab.HasHidden);
        Assert.All(tab.Hidden, h => Assert.Equal(["cat_ears"], h.Tags));

        var reportVm = new BlacklistReportViewModel(tab, services);
        Assert.Equal(3, reportVm.Items.Count);
        Assert.Equal("cat_ears", Assert.Single(reportVm.Tags).Tag);
        await Harness.WaitFor(() => reportVm.Items.All(i => i.Thumbnail is not null));

        var reportWindow = new BlacklistReportWindow { DataContext = reportVm };
        reportWindow.Show();
        Harness.Capture(reportWindow, "blacklist-report");
        reportWindow.Hide();

        var kept = reportVm.Items[0];
        kept.ShowCommand.Execute(null);
        Assert.Equal(2, reportVm.Items.Count);
        Assert.Equal(5, tab.Posts.Count);
        Assert.Equal(2, tab.Hidden.Count);
        Assert.True(services.BlacklistAllowed.IsAllowed(kept.Hidden.Post.Key));

        vm.SearchCommand.Execute(null);
        var second = Assert.IsType<SearchTabViewModel>(vm.SelectedTab);
        await Harness.WaitFor(() => second.Posts.Count == 5 && second.Posts.All(p => p.Thumbnail is not null || p.ThumbnailFailed));
        Assert.Equal(2, second.Hidden.Count);

        reportVm.Dispose();
        services.BlacklistAllowed.Clear();
        services.Config.BlacklistedTags.Remove("cat_ears");
        main.Close();
    });
}

public class TrayTests
{
    private static DownloadItem Item(int id, DownloadStatus status, double progress = 0) =>
        new(new Post { Id = id, BooruBaseUrl = StubBooru.Host }) { Status = status, Progress = progress };

    [Fact]
    public void ProgressCountsOnlyWhatWasStarted()
    {
        Assert.False(DownloadTray.Measure([Item(1, DownloadStatus.Queued)]).Active);

        var snapshot = DownloadTray.Measure(
        [
            Item(1, DownloadStatus.Done), Item(2, DownloadStatus.Downloading, 0.5),
            Item(3, DownloadStatus.Failed), Item(4, DownloadStatus.Downloading, 0), Item(5, DownloadStatus.Queued),
        ]);
        Assert.True(snapshot.Active);
        Assert.Equal(4, snapshot.Started);
        Assert.Equal(2, snapshot.Finished);
        Assert.Equal(62.5, snapshot.Progress * 100, tolerance: 0.01);

        var finished = DownloadTray.Measure([Item(1, DownloadStatus.Done), Item(2, DownloadStatus.Skipped)]);
        Assert.False(finished.Active);
        Assert.Equal(100, finished.Percent);
    }

    [Fact]
    public Task IconShowsTheRingForEveryThemeAndState() => Harness.Run(async () =>
    {
        var services = Harness.NewServices();
        var strip = new StackPanel { Orientation = Avalonia.Layout.Orientation.Vertical, Spacing = 12, Margin = new Thickness(16) };
        foreach (var theme in new[] { "Dark", "Light", "Sakura" })
        {
            services.Theme.Apply(services.Theme.Store.Find(theme));
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12 };
            foreach (var (progress, state) in new[] { (0.0, TrayState.Downloading), (0.25, TrayState.Downloading), (0.6, TrayState.Downloading), (0.9, TrayState.Downloading), (1.0, TrayState.Done) })
            {
                var bitmap = TrayIconRenderer.Render(progress, state, TrayIconRenderer.ThemePalette());
                Assert.Equal(TrayIconRenderer.Size, bitmap.PixelSize.Width);
                row.Children.Add(new Image { Source = bitmap, Width = 64, Height = 64 });
                row.Children.Add(new Image { Source = bitmap, Width = 24, Height = 24, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            }
            strip.Children.Add(new Border { Background = Avalonia.Media.Brushes.Transparent, Child = row });
        }
        var window = new Window { Width = 760, Height = 300, Content = strip };
        window.Show();
        Harness.Capture(window, "tray-icons");
        services.Theme.Apply(services.Theme.Store.Find("Dark"));
        window.Close();
        await Task.CompletedTask;
    });
}

public class TabScrollTests
{
    [Fact]
    public Task SwitchingTabsKeepsTheScrollPosition() => Harness.Run(async () =>
    {
        var services = Harness.NewServices();
        var vm = new MainViewModel(services);
        var window = new MainWindow { DataContext = vm, Width = 900, Height = 560 };
        window.Show();
        vm.SearchCommand.Execute(null);
        var search = Assert.IsType<SearchTabViewModel>(vm.SelectedTab);
        await Harness.WaitFor(() => search.Posts.Count == 7 && search.Posts.All(p => p.Thumbnail is not null));
        search.TileSize = 360;
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        ScrollViewer Scroller() => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window)
            .OfType<ListBox>().First(l => l.Name == "Grid" && l.DataContext == search)
            .GetVisualDescendants().OfType<ScrollViewer>().First();

        var scroller = Scroller();
        scroller.Offset = new Vector(0, 500);
        window.UpdateLayout();
        Assert.True(scroller.Offset.Y > 100, "the grid should be tall enough to scroll");
        var before = scroller.Offset.Y;

        vm.OpenFavorites();
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        vm.SelectedTab = search;
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(before, Scroller().Offset.Y);
        window.Close();
    });

    [Fact]
    public Task SaveStateReopensTheSameTabsAndScrollPosition() => Harness.Run(async () =>
    {
        var services = Harness.NewIsolatedServices();
        services.Session.SetEnabled(true);
        var vm = new MainViewModel(services);
        foreach (var open in vm.Tabs.ToList())
        {
            vm.CloseTabCommand.Execute(open); // "continue reading" may have opened one; this test counts tabs
        }
        var window = new MainWindow { DataContext = vm, Width = 900, Height = 560 };
        window.Show();
        vm.SearchText = "cat";
        vm.SearchCommand.Execute(null);
        var search = Assert.IsType<SearchTabViewModel>(vm.SelectedTab);
        await Harness.WaitFor(() => search.Posts.Count == 7 && search.Posts.All(p => p.Thumbnail is not null));
        search.TileSize = 360;
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        ScrollViewer Scroller(Window w, TabViewModel t) => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(w)
            .OfType<ListBox>().First(l => l.Name == "Grid" && l.DataContext == t)
            .GetVisualDescendants().OfType<ScrollViewer>().First();
        Scroller(window, search).Offset = new Vector(0, 500);
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var scrolled = Scroller(window, search).Offset.Y;
        Assert.True(scrolled > 100);
        vm.OpenFolders();
        vm.SearchText = "dog";
        vm.SaveSession();
        window.Close();

        // a new run: same services, same files
        var again = new MainViewModel(services);
        Assert.Equal(["search", "folders"], again.Tabs.Select(t => t switch
        {
            SearchTabViewModel => "search",
            FoldersTabViewModel => "folders",
            _ => "?",
        }));
        Assert.IsType<FoldersTabViewModel>(again.SelectedTab);
        Assert.Equal("dog", again.SearchText);
        var restored = again.Tabs.OfType<SearchTabViewModel>().Single();
        Assert.Equal("cat", restored.Tags);
        Assert.Equal(scrolled, restored.PendingScroll);

        var second = new MainWindow { DataContext = again, Width = 900, Height = 560 };
        second.Show();
        await Harness.WaitFor(() => restored.Posts.Count == 7);
        restored.TileSize = 360;
        again.SelectedTab = restored;
        for (var i = 0; i < 20 && restored.PendingScroll > 0; i++)
        {
            second.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(50);
        }
        Assert.Equal(0, restored.PendingScroll);
        Assert.Equal(scrolled, Scroller(second, restored).Offset.Y, 1);
        second.Close();

        services.Session.SetEnabled(false);
        // Off again: nothing is restored ("continue reading" may still open its own tab from the shared database).
        Assert.DoesNotContain(new MainViewModel(services).Tabs, t => t is SearchTabViewModel or FoldersTabViewModel);
    });

    [Fact]
    public Task RatingFilterHidesTilesFromTheGridViewerAndQueue() => Harness.Run(async () =>
    {
        var services = Harness.NewServices();
        var vm = new MainViewModel(services);
        var window = new MainWindow { DataContext = vm, Width = 900, Height = 560 };
        window.Show();
        vm.SearchCommand.Execute(null);
        var tab = Assert.IsType<SearchTabViewModel>(vm.SelectedTab);
        await Harness.WaitFor(() => tab.Posts.Count == 7);
        for (var i = 0; i < tab.Posts.Count; i++)
        {
            tab.Posts[i].Post.Rating = i < 2 ? "e" : i < 4 ? "q" : "s";
        }

        tab.ShowExplicit = false;
        Assert.Equal(5, tab.ShownPosts.Count());
        Assert.False(tab.Posts[0].Shown);
        tab.AddAllLoadedCommand.Execute(null);
        // The queue is persisted in the shared test database, so only look at this grid's posts and clean up.
        Assert.Equal(5, tab.Posts.Count(p => services.Queue.Contains(p.Post.Key)));
        Assert.False(services.Queue.Contains(tab.Posts[0].Post.Key));
        foreach (var item in services.Queue.Items.Where(i => tab.Posts.Any(p => p.Post.Key == i.Key)).ToList())
        {
            services.Queue.Remove(item);
        }

        tab.ShowQuestionable = false;
        Assert.Equal(3, tab.ShownPosts.Count());
        tab.ShowExplicit = tab.ShowQuestionable = true;
        Assert.Equal(7, tab.ShownPosts.Count());
        window.Close();
    });

    [Fact]
    public Task SortReordersLoadedPostsAndUndoes() => Harness.Run(async () =>
    {
        var services = Harness.NewServices();
        var vm = new MainViewModel(services);
        var window = new MainWindow { DataContext = vm, Width = 900, Height = 560 };
        window.Show();
        vm.SearchCommand.Execute(null);
        var tab = Assert.IsType<SearchTabViewModel>(vm.SelectedTab);
        await Harness.WaitFor(() => tab.Posts.Count == 7);
        var arrival = tab.Posts.Select(p => p.Post.Key).ToList();
        for (var i = 0; i < tab.Posts.Count; i++)
        {
            tab.Posts[i].Post.Score = (i * 5) % 7;
        }

        tab.Sort = PostSort.Score;
        Assert.Equal(tab.Posts.Select(p => p.Post.Score).OrderByDescending(x => x), tab.Posts.Select(p => p.Post.Score));
        tab.Sort = PostSort.LoadOrder;
        Assert.Equal(arrival, tab.Posts.Select(p => p.Post.Key));
        window.Close();
    });

    [Fact]
    public Task SafeModeStartsSearchesWithoutExplicitAndSavedSearchesRun() => Harness.Run(async () =>
    {
        var services = Harness.NewIsolatedServices();
        var vm = new MainViewModel(services);
        var window = new MainWindow { DataContext = vm, Width = 900, Height = 560 };
        window.Show();
        var booru = vm.SelectedBooru!;
        try
        {
            vm.ToggleSafeMode();
            vm.SearchText = "cat";
            vm.SearchCommand.Execute(null);
            var tab = Assert.IsType<SearchTabViewModel>(vm.SelectedTab);
            Assert.False(tab.ShowExplicit);
            Assert.True(tab.ShowSafe);

            vm.ToggleSafeMode();
            Assert.True(tab.ShowExplicit);

            vm.SaveCurrentSearch("my cats");
            vm.SearchText = "";
            vm.RunSavedSearch(services.Prefs.SavedSearches.Single(x => x.Name == "my cats"));
            Assert.Equal("cat", Assert.IsType<SearchTabViewModel>(vm.SelectedTab).Tags);
        }
        finally
        {
            services.Prefs.SetSafeMode(booru.Identity, false);
            services.Prefs.DeleteSearch("my cats");
            window.Close();
        }
        await Task.CompletedTask;
    });

    [Fact]
    public Task ReaderRemembersZoomAndBookmarksPerPool() => Harness.Run(async () =>
    {
        var services = Harness.NewIsolatedServices();
        var booru = services.Config.BooruConfigurations[0];
        services.Config.ReaderMode = "PAGED"; // other tests toggle these in the shared config
        services.Config.ReaderSpread = false;
        // A pool key of its own, so parallel tests that read the stub pool do not share this memory.
        var pool = new Pool(4242, "Remember me", null, "series", StubBooru.PoolPages, [101, 102, 103, 104], "https://remember.example", null, null);
        var first = new ReaderViewModel(services, pool, booru, 0);
        await Harness.WaitFor(() => first.Pages.Count == StubBooru.PoolPages);
        try
        {
            Assert.Equal(1, first.Zoom);
            first.ZoomBy(2);
            first.Step(1);
            first.ToggleBookmarkCommand.Execute(null);
            Assert.True(first.IsBookmarked);
            first.Step(1);
            Assert.False(first.IsBookmarked);
            first.Dispose();

            var second = new ReaderViewModel(services, pool, booru, 0);
            await Harness.WaitFor(() => second.Pages.Count == StubBooru.PoolPages);
            Assert.Equal(2, second.Zoom);
            Assert.Equal([1], second.Bookmarks);
            second.GoToPage(1);
            Assert.True(second.IsBookmarked);
            second.ToggleBookmarkCommand.Execute(null);
            Assert.False(second.HasBookmarks);
            second.Dispose();
        }
        finally
        {
            services.Prefs.SetZoom(pool.Key, 1);
            if (services.Prefs.BookmarksOf(pool.Key).Contains(1))
            {
                services.Prefs.ToggleBookmark(pool.Key, 1);
            }
        }
    });

    [Fact]
    public Task MergedSearchMixesBoorusSkipsDuplicatesAndReportsFailures() => Harness.Run(async () =>
    {
        var services = Harness.NewIsolatedServices();
        var good = services.Config.BooruConfigurations[0];
        var twin = new Kurotsuki.Core.Config.BooruConfiguration
        {
            Engine = Kurotsuki.Core.Booru.BooruEngine.DanbooruSoftware, CustomBaseUrl = StubBooru.Host + "/second",
        };
        var broken = new Kurotsuki.Core.Config.BooruConfiguration
        {
            Engine = Kurotsuki.Core.Booru.BooruEngine.Szurubooru, CustomBaseUrl = StubBooru.Host,
        };
        var tab = new MergedSearchTabViewModel(services, [good, twin, broken], "cat");
        await tab.LoadNextPageAsync();

        // the stub answers every booru with the same seven files: each is shown once, and the booru it cannot read is named
        Assert.Equal(7, tab.Posts.Count);
        Assert.Contains(broken.ShortName, tab.Status);
        Assert.Equal(7, tab.Posts.Select(p => p.Post.Image).Distinct().Count());
        Assert.Equal("cat", tab.Tags);
        tab.Dispose();
    });

    [Fact]
    public Task FilterBoxPastedLinksWatchedSearchesAndPrivacyModeWork() => Harness.Run(async () =>
    {
        var services = Harness.NewIsolatedServices();
        var vm = new MainViewModel(services);
        var window = new MainWindow { DataContext = vm, Width = 1000, Height = 640 };
        window.Show();

        // a pasted link opens the post instead of searching for the text
        vm.SearchText = StubBooru.Host + "/posts/3";
        vm.SearchCommand.Execute(null);
        await Harness.WaitFor(() => vm.Viewer is not null);
        var opened = vm.Viewer!;
        Assert.Equal(3, ((ViewerViewModel)opened.DataContext!).Post.Id);
        Assert.Empty(vm.Tabs.OfType<SearchTabViewModel>());
        opened.Close();

        // a normal search keeps working and the grid filter narrows the loaded posts by tag
        vm.SearchText = "cat";
        vm.SearchCommand.Execute(null);
        var tab = Assert.IsType<SearchTabViewModel>(vm.SelectedTab);
        await Harness.WaitFor(() => tab.Posts.Count == 7);
        tab.FilterVisible = true;
        tab.FilterText = "cat_ears";
        Assert.Equal(3, tab.ShownPosts.Count()); // posts 2, 4 and 6 are tagged cat_ears
        tab.FilterText = "-cat_ears";
        Assert.Equal(4, tab.ShownPosts.Count());
        tab.FilterVisible = false; // hiding the box clears the filter
        Assert.Equal(7, tab.ShownPosts.Count());

        // watched searches: the first check only records where the search stands
        vm.WatchCurrentSearch();
        await vm.CheckWatchedAsync();
        Assert.Equal(7, services.Prefs.Watched.Single().LastSeenId);
        Assert.Empty(vm.WatchedNews);
        services.Prefs.Watch(services.Prefs.Watched.Single() with { LastSeenId = 5 });
        await vm.CheckWatchedAsync();
        Assert.Equal(2, vm.WatchedNews["cat"]); // posts 6 and 7 are newer than 5
        vm.OpenWatched(services.Prefs.Watched.Single());
        Assert.Empty(vm.WatchedNews);
        Assert.Equal(7, services.Prefs.Watched.Single().LastSeenId);

        // privacy mode puts the class on the window that the blur style selects
        vm.PrivacyMode = true;
        Assert.Contains("privacy", window.Classes);
        vm.PrivacyMode = false;
        Assert.DoesNotContain("privacy", window.Classes);
        window.Close();
    });

    [Fact]
    public void PaletteFiltersByEveryTypedWord()
    {
        PaletteEntry E(string title, string group) => new(title, group, () => { });
        var all = new[] { E("Favourite posts", "Actions"), E("cat ears", "Favourite tags"), E("Settings", "Actions") };
        Assert.Equal(3, CommandPaletteWindow.Filter(all, "").Count);
        Assert.Equal(["cat ears"], CommandPaletteWindow.Filter(all, "CAT tags").Select(e => e.Title));
        Assert.Equal(["Favourite posts", "cat ears"], CommandPaletteWindow.Filter(all, "favourite").Select(e => e.Title));
        Assert.Empty(CommandPaletteWindow.Filter(all, "zzz"));
    }

    [Fact]
    public Task SlideshowAdvancesAndStopsAtTheLastPost() => Harness.Run(async () =>
    {
        var services = Harness.NewIsolatedServices();
        var posts = Enumerable.Range(1, 3).Select(i => new Kurotsuki.Core.Booru.Post
        {
            Id = i, Image = $"i{i}.png", FileUrl = $"{StubBooru.Host}/media/i{i}.png", PreviewUrl = $"{StubBooru.Host}/media/t.png",
            BooruBaseUrl = StubBooru.Host,
        }).ToList();
        var viewer = new ViewerViewModel(services, posts, 0);
        viewer.SlideshowOn = true;
        viewer.SlideshowTick();
        Assert.Equal(1, viewer.Index);
        viewer.SlideshowTick();
        Assert.Equal(2, viewer.Index);
        viewer.SlideshowTick(); // at the last post it stops instead of wrapping around
        Assert.False(viewer.SlideshowOn);
        viewer.Dispose();
        await Task.CompletedTask;
    });

    [Fact]
    public Task TextSizeAndFollowSystemAreSavedFromTheSettings() => Harness.Run(async () =>
    {
        var services = Harness.NewIsolatedServices();
        try
        {
            var settings = new SettingsViewModel(services);
            Assert.Equal(14, settings.TextSize.Size);
            settings.TextSize = settings.TextSizes.Single(t => t.Size == 18);
            settings.Appearance.FollowSystem = true;
            settings.Save();
            Assert.Equal(18, services.Prefs.TextSize);
            Assert.True(services.Theme.Store.FollowsSystem());
            Assert.Equal(18.0, Application.Current!.Resources["KtBaseFontSize"]);
        }
        finally
        {
            Kurotsuki.App.Services.ThemeManager.ApplyTextSize(14);
            services.Theme.Store.SetFollowSystem(false);
        }
        await Task.CompletedTask;
    });
}
