using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Kurotsuki.App.Views;

/// <summary>One line of the command palette: what is shown, which kind it is, and what choosing it does.</summary>
public sealed record PaletteEntry(string Title, string Group, Action Run);

/// <summary>Ctrl+K: one box that finds tabs, folders, tags, searches and actions by what is typed.</summary>
public partial class CommandPaletteWindow : Window
{
    private readonly IReadOnlyList<PaletteEntry> _all;

    public CommandPaletteWindow() : this([])
    {
    }

    public CommandPaletteWindow(IReadOnlyList<PaletteEntry> entries)
    {
        InitializeComponent();
        _all = entries;
        Results.ItemsSource = _all;
        Results.SelectedIndex = _all.Count > 0 ? 0 : -1;
        Opened += (_, _) => Query.Focus();
        Query.TextChanged += (_, _) => Refresh();
        Query.KeyDown += OnQueryKey;
        Results.DoubleTapped += (_, _) => Run();
        Deactivated += (_, _) => Close();
    }

    /// <summary>Every typed word must appear in the title or the group (case-insensitive); the order is kept.</summary>
    public static IReadOnlyList<PaletteEntry> Filter(IEnumerable<PaletteEntry> entries, string? text)
    {
        var words = (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return [.. entries.Where(e => words.All(w =>
            e.Title.Contains(w, StringComparison.CurrentCultureIgnoreCase) || e.Group.Contains(w, StringComparison.CurrentCultureIgnoreCase)))];
    }

    private void Refresh()
    {
        var shown = Filter(_all, Query.Text);
        Results.ItemsSource = shown;
        Results.SelectedIndex = shown.Count > 0 ? 0 : -1;
    }

    private void OnQueryKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.Enter: Run(); break;
            case Key.Down: Results.SelectedIndex = Math.Min(Results.ItemCount - 1, Results.SelectedIndex + 1); break;
            case Key.Up: Results.SelectedIndex = Math.Max(0, Results.SelectedIndex - 1); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Run()
    {
        if (Results.SelectedItem is PaletteEntry entry)
        {
            Close();
            entry.Run();
        }
    }
}
