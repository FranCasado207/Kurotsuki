using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Kurotsuki.App.Services;
using Kurotsuki.App.ViewModels;
using Kurotsuki.Core.I18n;

namespace Kurotsuki.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
            {
                SearchBox.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.F && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && Main?.SelectedTab is PostGridViewModel grid)
            {
                grid.FilterVisible = !grid.FilterVisible;
                e.Handled = true;
            }
            else if (e.Key == Key.K && e.KeyModifiers == KeyModifiers.Control)
            {
                OpenPalette();
                e.Handled = true;
            }
            else if (e.Key == Key.F9 && Main is { } main)
            {
                main.PrivacyMode = !main.PrivacyMode;
                e.Handled = true;
            }
        };
        SearchBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && !SearchBox.IsDropDownOpen && Main is { } main)
            {
                main.SearchCommand.Execute(null);
                e.Handled = true;
            }
        };
        SearchBox.TextSelector = (_, item) => item ?? "";
        DataContextChanged += (_, _) => Attach();
        Closing += (_, _) => SaveLayout();
    }

    private MainViewModel? Main => DataContext as MainViewModel;
    private AppServices? Services => (DataContext as MainViewModel)?.Services;

    private void Attach()
    {
        if (Main is not { } main)
        {
            return;
        }
        SearchBox.AsyncPopulator = main.SuggestAsync;
        var layout = main.Services.Layout;
        Width = layout.Get("window.width", Width);
        Height = layout.Get("window.height", Height);
        SidebarPanel.Width = layout.Get("sidebar.width", SidebarPanel.Width);
        QueuePanel.Width = layout.Get("queue.width", QueuePanel.Width);
        if (layout.Flag("window.maximized", false))
        {
            WindowState = WindowState.Maximized;
        }
    }

    /// <summary>Window and panel sizes are remembered in config.json (panelSizes, "next.*" keys).</summary>
    private void SaveLayout()
    {
        if (Services is not { } services)
        {
            return;
        }
        var layout = services.Layout;
        layout.SetFlag("window.maximized", WindowState == WindowState.Maximized);
        if (WindowState == WindowState.Normal)
        {
            layout.Set("window.width", Width);
            layout.Set("window.height", Height);
        }
        layout.Set("sidebar.width", SidebarPanel.Bounds.Width > 1 ? SidebarPanel.Bounds.Width : SidebarPanel.Width);
        layout.Set("queue.width", QueuePanel.Bounds.Width > 1 ? QueuePanel.Bounds.Width : QueuePanel.Width);
        services.SaveConfig();
    }

    // ---- menu -----------------------------------------------------------------------------------------------

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (Main is not { } main)
        {
            return;
        }
        var settings = new SettingsWindow { DataContext = new SettingsViewModel(main.Services) };
        await settings.ShowDialog(this);
        main.ReloadBoorus();
    }

    private async void OnExportBackupClick(object? sender, RoutedEventArgs e)
    {
        if (Services is { } services)
        {
            await BackupActions.ExportAsync(this, services);
        }
    }

    private async void OnImportBackupClick(object? sender, RoutedEventArgs e)
    {
        if (Services is { } services && await BackupActions.ImportAsync(this, services))
        {
            Main?.ReloadBoorus();
        }
    }

    private async void OnTransferClick(object? sender, RoutedEventArgs e)
    {
        if (Services is { } services)
        {
            await new TransferWindow { DataContext = new TransferViewModel(services) }.ShowDialog(this);
        }
    }

    private async void OnExportLogsClick(object? sender, RoutedEventArgs e)
    {
        if (Services is { } services)
        {
            await BackupActions.ExportLogsAsync(this, services);
        }
    }

    private void OnHistoryClick(object? sender, RoutedEventArgs e)
    {
        if (Main is not { } main || sender is not Control button)
        {
            return;
        }
        var history = main.Services.History;
        var items = new List<Control>();
        foreach (var query in history.Items)
        {
            var item = new MenuItem { Header = query };
            item.Click += (_, _) => main.SearchSelected(query);
            items.Add(item);
        }
        if (items.Count == 0)
        {
            items.Add(new MenuItem { Header = Messages.Get("next.historyEmpty"), IsEnabled = false });
        }
        else
        {
            items.Add(new Separator());
            var clear = new MenuItem { Header = Messages.Get("next.historyClear") };
            clear.Click += (_, _) => history.Clear();
            items.Add(clear);
        }
        items.Add(new Separator());
        items.AddRange(SavedSearchItems(main));
        Menus.Show(button, items);
    }

    private async void OpenPalette()
    {
        if (Main is { } main)
        {
            await new CommandPaletteWindow(PaletteEntries(main)).ShowDialog(this);
        }
    }

    private List<PaletteEntry> PaletteEntries(MainViewModel main)
    {
        var entries = new List<PaletteEntry>();
        void Add(string title, Action run, string? group = null) => entries.Add(new PaletteEntry(title, group ?? "", run));
        Add(Messages.Get("menu.settings"), () => OnSettingsClick(this, new RoutedEventArgs()), Messages.Get("next.paletteActions"));
        Add(Messages.Get("favorites.tab"), main.OpenFavorites, Messages.Get("next.paletteActions"));
        Add(Messages.Get("folders.tab"), main.OpenFolders, Messages.Get("next.paletteActions"));
        Add(Messages.Get("reading.tab"), main.OpenReading, Messages.Get("next.paletteActions"));
        Add(Messages.Get("next.railTags"), () => main.SidebarVisible = !main.SidebarVisible, Messages.Get("next.paletteActions"));
        Add(Messages.Get("next.downloadsTip"), () => main.QueueVisible = !main.QueueVisible, Messages.Get("next.paletteActions"));
        Add(Messages.Get("next.privacyMode"), () => main.PrivacyMode = !main.PrivacyMode, Messages.Get("next.paletteActions"));
        Add(Messages.Get("next.statsTitle"), () => _ = LibraryActions.ShowStatsAsync(this, main.Services), Messages.Get("next.paletteActions"));
        Add(Messages.Get("next.exportFavorites"), () => _ = LibraryActions.ExportCsvAsync(this, main.Services), Messages.Get("next.paletteActions"));
        Add(Messages.Get("menu.reloadBoorus"), main.ReloadBoorus, Messages.Get("next.paletteActions"));
        foreach (var tab in main.Tabs.ToList())
        {
            Add(tab.Title, () => main.SelectedTab = tab, Messages.Get("next.paletteTabs"));
        }
        foreach (var folder in main.Services.Folders.Names.ToList())
        {
            Add(folder, () => main.OpenFolder(folder), Messages.Get("folders.tab"));
        }
        foreach (var tag in main.Services.Favorites.Tags.ToList())
        {
            Add(tag, () => main.SearchSelected(tag), Messages.Get("next.paletteTags"));
        }
        foreach (var saved in main.Services.Prefs.SavedSearches.ToList())
        {
            Add(saved.Name, () => main.RunSavedSearch(saved), Messages.Get("next.savedSearches"));
        }
        foreach (var watched in main.Services.Prefs.Watched.ToList())
        {
            Add(watched.Name, () => main.OpenWatched(watched), Messages.Get("next.watchedSearches"));
        }
        foreach (var query in main.Services.History.Items.Take(15))
        {
            Add(query, () => main.SearchSelected(query), Messages.Get("next.history"));
        }
        return entries;
    }

    private IEnumerable<Control> SavedSearchItems(MainViewModel main)
    {
        var prefs = main.Services.Prefs;
        var saved = new MenuItem { Header = Messages.Get("next.savedSearches"), IsEnabled = prefs.SavedSearches.Count > 0 };
        var delete = new MenuItem { Header = Messages.Get("next.deleteSavedSearch"), IsEnabled = prefs.SavedSearches.Count > 0 };
        foreach (var search in prefs.SavedSearches.ToList())
        {
            var run = new MenuItem { Header = search.Name };
            run.Click += (_, _) => main.RunSavedSearch(search);
            saved.Items.Add(run);
            var remove = new MenuItem { Header = search.Name };
            remove.Click += (_, _) => prefs.DeleteSearch(search.Name);
            delete.Items.Add(remove);
        }
        var save = new MenuItem { Header = Messages.Get("next.saveSearch"), IsEnabled = !string.IsNullOrWhiteSpace(main.SearchText) };
        save.Click += async (_, _) =>
        {
            var name = await Dialogs.PromptAsync(this, Messages.Get("next.saveSearch"), Messages.Get("next.saveSearchName"), main.SearchText.Trim());
            if (!string.IsNullOrWhiteSpace(name))
            {
                main.SaveCurrentSearch(name.Trim());
            }
        };
        var watch = new MenuItem { Header = Messages.Get("next.watchSearch"), IsEnabled = !string.IsNullOrWhiteSpace(main.SearchText) && main.SelectedBooru is not null };
        watch.Click += (_, _) => main.WatchCurrentSearch();
        var watched = new MenuItem { Header = Messages.Get("next.watchedSearches"), IsEnabled = prefs.Watched.Count > 0 };
        var stop = new MenuItem { Header = Messages.Get("next.stopWatching"), IsEnabled = prefs.Watched.Count > 0 };
        foreach (var w in prefs.Watched.ToList())
        {
            var news = main.WatchedNews.GetValueOrDefault(w.Name);
            var open = new MenuItem { Header = news > 0 ? Messages.Get("next.newSuffix", w.Name, news) : w.Name };
            open.Click += (_, _) => main.OpenWatched(w);
            watched.Items.Add(open);
            var remove = new MenuItem { Header = w.Name };
            remove.Click += (_, _) => main.StopWatching(w.Name);
            stop.Items.Add(remove);
        }
        return [save, saved, delete, new Separator(), watch, watched, stop];
    }

    private void OnRailSearchClick(object? sender, RoutedEventArgs e)
    {
        SearchBox.Focus();
        if (Main is { } main && main.Tabs.OfType<SearchTabViewModel>().LastOrDefault() is { } tab)
        {
            main.SelectedTab = tab;
        }
    }

    /// <summary>The old menu bar, folded into one menu: the window is for browsing, these are occasional.</summary>
    private void OnMoreClick(object? sender, RoutedEventArgs e)
    {
        if (Main is not { } main || sender is not Control button)
        {
            return;
        }
        Control Item(string key, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = Messages.Get(key), IsEnabled = enabled };
            item.Click += (_, _) => action();
            return item;
        }
        Control Handler(string key, EventHandler<RoutedEventArgs> click) =>
            Item(key, () => click(this, new RoutedEventArgs()));
        Menus.Show(button,
        [
            Handler("menu.settings", OnSettingsClick),
            Item("menu.downloadResults", () => OnBulkClick(this, new RoutedEventArgs()), main.CanBulkDownload),
            Item("menu.reloadBoorus", main.ReloadBoorus),
            SafeModeItem(main),
            new Separator(),
            Handler("menu.exportBackup", OnExportBackupClick),
            Handler("menu.importBackup", OnImportBackupClick),
            Handler("menu.sendToPhone", OnTransferClick),
            Handler("next.exportLogs", OnExportLogsClick),
            Item("next.statsTitle", () => _ = LibraryActions.ShowStatsAsync(this, main.Services)),
            Item("next.exportFavorites", () => _ = LibraryActions.ExportCsvAsync(this, main.Services)),
            Item("next.paletteOpen", OpenPalette),
            new Separator(),
            Item("menu.openDownloads", () => main.OpenDownloadsCommand.Execute(null)),
            Item("menu.openData", () => main.OpenDataCommand.Execute(null)),
            Item("settings.forgetDownloads", () => main.ForgetHistoryCommand.Execute(null)),
            new Separator(),
            Handler("menu.about", OnAboutClick),
            Handler("menu.exit", OnExitClick),
        ]);
    }

    private Control SafeModeItem(MainViewModel main)
    {
        var item = new MenuItem
        {
            Header = Messages.Get("next.safeMode"), ToggleType = MenuItemToggleType.CheckBox,
            IsEnabled = main.SelectedBooru is not null,
            IsChecked = main.SelectedBooru is { } b && main.Services.Prefs.IsSafeMode(b.Identity),
        };
        item.Click += (_, _) => main.ToggleSafeMode();
        return item;
    }

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();
    private void OnReloadBoorusClick(object? sender, RoutedEventArgs e) => Main?.ReloadBoorus();

    private async void OnAboutClick(object? sender, RoutedEventArgs e) =>
        await new AboutWindow().ShowDialog(this);

    private async void OnBulkClick(object? sender, RoutedEventArgs e)
    {
        if (Main is { SelectedTab: SearchTabViewModel tab } main)
        {
            var window = new BulkDownloadWindow { DataContext = new BulkDownloadViewModel(main.Services, tab) };
            await window.ShowDialog(this);
        }
        else
        {
            await Dialogs.MessageAsync(this, Messages.Get("bulk.title"), Messages.Get("bulk.needSearch"));
        }
    }

    // ---- favourite tags ---------------------------------------------------------------------------------------

    private void OnFavoriteTagClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: FavoriteTagItem item } && Main is { } main)
        {
            main.SearchSelected(item.Tag);
        }
    }

    private void OnFavoriteTagContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Control { DataContext: FavoriteTagItem item } button && Main is { } main)
        {
            Menus.Show(button, Menus.ForTag(item.Tag, item.Group, main.Services, main.SearchSelected));
            e.Handled = true;
        }
    }

    private void OnGroupHeaderReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Control { DataContext: FavoriteSection { Group: { } group } } control || Main is not { } main)
        {
            return;
        }
        var button = e.GetCurrentPoint(control).Properties.PointerUpdateKind;
        if (button == PointerUpdateKind.RightButtonReleased)
        {
            Menus.Show(control, Menus.ForGroup(group, main.Services, main.SearchSelected));
        }
        else if (button == PointerUpdateKind.LeftButtonReleased && group.Tags.Count > 0)
        {
            main.SearchSelected(string.Join(' ', group.Tags));
        }
    }
}
