using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Kurotsuki.Core.I18n;

namespace Kurotsuki.App.Services;

/// <summary>Small modal dialogs (ask for text, confirm) built in code so they need no view model.</summary>
public static class Dialogs
{
    public static async Task<string?> PromptAsync(Window? owner, string title, string label, string initial = "")
    {
        var input = new TextBox { Text = initial, MinWidth = 320 };
        string? result = null;
        var ok = new Button { Content = Messages.Get("next.save"), IsDefault = true, Classes = { "accent" } };
        var cancel = new Button { Content = Messages.Get("next.cancel"), IsCancel = true };
        var window = Build(title, new StackPanel
        {
            Spacing = 14,
            Margin = new Thickness(24),
            Children =
            {
                new TextBlock { Text = label },
                input,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8,
                    Children = { cancel, ok },
                },
            },
        });
        ok.Click += (_, _) => { result = input.Text?.Trim(); window.Close(); };
        cancel.Click += (_, _) => window.Close();
        window.Opened += (_, _) => input.Focus();
        await Show(window, owner);
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    public static async Task<bool> ConfirmAsync(Window? owner, string title, string message)
    {
        var confirmed = false;
        var yes = new Button { Content = Messages.Get("next.yes"), IsDefault = true, Classes = { "accent" } };
        var no = new Button { Content = Messages.Get("next.no"), IsCancel = true };
        var window = Build(title, new StackPanel
        {
            Spacing = 18,
            Margin = new Thickness(24),
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 420 },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8,
                    Children = { no, yes },
                },
            },
        });
        yes.Click += (_, _) => { confirmed = true; window.Close(); };
        no.Click += (_, _) => window.Close();
        await Show(window, owner);
        return confirmed;
    }

    public static Task MessageAsync(Window? owner, string title, string message)
    {
        var close = new Button { Content = Messages.Get("next.close"), IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Classes = { "accent" } };
        var window = Build(title, new StackPanel
        {
            Spacing = 18,
            Margin = new Thickness(24),
            Children = { new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 460 }, close },
        });
        close.Click += (_, _) => window.Close();
        return Show(window, owner);
    }

    /// <summary>A longer text (a wiki page): scrolls instead of growing past the screen.</summary>
    public static Task ShowTextAsync(Window? owner, string title, string text)
    {
        var close = new Button { Content = Messages.Get("next.close"), IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Classes = { "accent" } };
        var body = new ScrollViewer
        {
            MaxHeight = 420,
            Content = new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 520, Margin = new Thickness(0, 0, 14, 0) },
        };
        var window = Build(title, new StackPanel { Spacing = 18, Margin = new Thickness(24), Children = { body, close } });
        close.Click += (_, _) => window.Close();
        return Show(window, owner);
    }

    private static Window Build(string title, Control content) => new()
    {
        Title = title,
        Content = content,
        SizeToContent = SizeToContent.WidthAndHeight,
        CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        ShowInTaskbar = false,
    };

    private static Task Show(Window window, Window? owner) =>
        owner is null ? Task.CompletedTask.ContinueWith(_ => { window.Show(); }) : window.ShowDialog(owner);
}
