using CommunityToolkit.Mvvm.ComponentModel;

namespace Kurotsuki.App.ViewModels;

/// <summary>A tab of the main window. <see cref="Dispose"/> runs when the tab is closed.</summary>
public abstract partial class TabViewModel : ObservableObject, IDisposable
{
    [ObservableProperty] private string _title = "";

    /// <summary>Set by the main window; every open tab keeps its view alive and only the selected one is visible.</summary>
    [ObservableProperty] private bool _isSelected;

    public virtual void Dispose()
    {
    }
}
