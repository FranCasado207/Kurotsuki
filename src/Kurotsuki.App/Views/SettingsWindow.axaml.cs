using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Kurotsuki.App.ViewModels;

namespace Kurotsuki.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        // Themes preview live, so closing without saving has to put the previous one back.
        Closed += (_, _) =>
        {
            if (Settings?.Appearance is { Committed: false } appearance)
            {
                appearance.Revert();
            }
        };
    }

    private SettingsViewModel? Settings => DataContext as SettingsViewModel;

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        Settings?.Save();
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private async void OnBrowseDownloads(object? sender, RoutedEventArgs e)
    {
        if (Settings is { } s && await PickFolderAsync() is { } folder)
        {
            s.DownloadFolder = folder;
        }
    }

    private async void OnBrowseCache(object? sender, RoutedEventArgs e)
    {
        if (Settings is { } s && await PickFolderAsync() is { } folder)
        {
            s.CacheFolder = folder;
        }
    }

    private async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }
}
