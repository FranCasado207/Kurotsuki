using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kurotsuki.App.Services;
using Kurotsuki.Core.I18n;
using Kurotsuki.Core.Theming;

namespace Kurotsuki.App.ViewModels;

/// <summary>One colour token in the editor. A hex that is not valid yet is simply not applied.</summary>
public sealed partial class TokenEditViewModel(string token, string hex) : ObservableObject
{
    public string Token { get; } = token;
    public string Label { get; } = Messages.Get("next.token." + token);
    [ObservableProperty] private string _hex = hex;
    public bool Valid => ThemeDefinition.IsHexColor(Hex.Trim());
    partial void OnHexChanged(string value) => OnPropertyChanged(nameof(Valid));
}

/// <summary>Picks, previews and edits themes. Everything previews live; nothing is kept until the settings are saved.</summary>
public sealed partial class ThemeEditorViewModel : ObservableObject
{
    private readonly ThemeManager _manager;
    private readonly ThemeDefinition _original;
    private bool _loading;

    public ThemeEditorViewModel(ThemeManager manager)
    {
        _manager = manager;
        _original = manager.Current;
        _followSystem = manager.Store.FollowsSystem();
        Reload(_original.Name);
    }

    public ObservableCollection<ThemeDefinition> Themes { get; } = [];
    public ObservableCollection<TokenEditViewModel> Tokens { get; } = [];

    [ObservableProperty] private ThemeDefinition? _selected;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private bool _dark = true;
    [ObservableProperty] private string _message = "";

    /// <summary>Use the system's dark/light setting instead of the selected theme (previewed at once).</summary>
    [ObservableProperty] private bool _followSystem;

    partial void OnFollowSystemChanged(bool value)
    {
        if (!_loading)
        {
            _manager.Apply(value ? _manager.ForSystem() : Selected ?? _original);
        }
    }

    public bool IsCustom => Selected is { BuiltIn: false };
    public string FolderPath => _manager.Store.Folder;

    private void Reload(string select)
    {
        Themes.Clear();
        foreach (var theme in _manager.Store.All())
        {
            Themes.Add(theme);
        }
        Selected = Themes.FirstOrDefault(t => t.Name.Equals(select, StringComparison.OrdinalIgnoreCase)) ?? Themes[0];
    }

    partial void OnSelectedChanged(ThemeDefinition? value)
    {
        OnPropertyChanged(nameof(IsCustom));
        if (value is null)
        {
            return;
        }
        _loading = true;
        Name = value.BuiltIn ? "" : value.Name;
        Dark = value.IsDark;
        Tokens.Clear();
        foreach (var token in ThemeDefinition.Tokens)
        {
            var item = new TokenEditViewModel(token, value.Color(token));
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TokenEditViewModel.Hex))
                {
                    Preview();
                }
            };
            Tokens.Add(item);
        }
        _loading = false;
        _manager.Apply(value);
    }

    partial void OnDarkChanged(bool value) => Preview();

    private ThemeDefinition Build(string name) => new(name, Dark, Tokens
        .Where(t => t.Valid)
        .ToDictionary(t => t.Token, t => t.Hex.Trim().ToUpperInvariant()));

    private void Preview()
    {
        if (!_loading)
        {
            _manager.Apply(Build(string.IsNullOrWhiteSpace(Name) ? "Preview" : Name.Trim()));
        }
    }

    /// <summary>Starts a new custom theme from whatever is on screen.</summary>
    [RelayCommand]
    private void New()
    {
        _loading = true;
        Name = Messages.Get("next.themeNewName");
        Message = "";
        _loading = false;
        Selected = null;
        OnPropertyChanged(nameof(IsCustom));
    }

    [RelayCommand]
    private void SaveTheme()
    {
        var name = Name.Trim();
        if (name.Length == 0)
        {
            Message = Messages.Get("next.themeNeedsName");
            return;
        }
        if (Tokens.Any(t => !t.Valid))
        {
            Message = Messages.Get("next.themeBadColor");
            return;
        }
        try
        {
            _manager.Store.Save(Build(name));
            Message = Messages.Get("next.themeSaved", name);
            Reload(name);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Message = e.Message;
        }
    }

    [RelayCommand]
    private void DeleteTheme()
    {
        if (Selected is { BuiltIn: false } theme)
        {
            _manager.Store.Delete(theme.Name);
            Reload(ThemeDefinition.DarkName);
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        Directory.CreateDirectory(_manager.Store.Folder);
        Shell.OpenFolder(_manager.Store.Folder);
    }

    public bool Committed { get; private set; }

    /// <summary>Keeps the chosen theme (called when the settings are saved). An unsaved draft is not kept.</summary>
    public void Commit()
    {
        if (Selected is { } theme)
        {
            _manager.Store.Select(theme.Name);
            Committed = true;
        }
        _manager.Store.SetFollowSystem(FollowSystem);
    }

    /// <summary>Puts back the theme that was active when the settings opened (cancel or close without saving).</summary>
    public void Revert() => _manager.Apply(_original);
}
