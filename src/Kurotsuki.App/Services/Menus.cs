using Avalonia.Controls;
using Kurotsuki.App.ViewModels;
using Kurotsuki.Core.I18n;
using Kurotsuki.Core.Library;

namespace Kurotsuki.App.Services;

/// <summary>Builds context menus in code, so their items can depend on live state (favourite or not, which folders exist…).</summary>
public static class Menus
{
    /// <summary>The menu most recently opened (tests look at it).</summary>
    public static ContextMenu? LastOpened { get; private set; }
    internal static void ClearLast() => LastOpened = null;

    /// <summary>
    /// Opens a context menu built right now. Menus are built on <c>ContextRequested</c> instead of filling a XAML
    /// <c>ContextMenu</c> in <c>Opening</c>: an empty menu never opens, so filling it that late showed nothing.
    /// </summary>
    public static void Show(Control target, IEnumerable<Control> items)
    {
        var menu = new ContextMenu { ItemsSource = items.ToList() };
        LastOpened = menu;
        menu.Open(target);
    }

    private static MenuItem Item(string header, Action onClick, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += (_, _) => onClick();
        return item;
    }

    public static List<Control> ForPost(PostViewModel post, AppServices services)
    {
        var items = new List<Control>
        {
            Item(Messages.Get("folders.open"), () => post.Owner?.OpenViewer(post), post.Owner is not null),
            Item(post.InQueue ? Messages.Get("grid.removeFromQueue") : Messages.Get("grid.addToQueue"), post.ToggleQueue),
            Item(post.IsFavorite ? Messages.Get("common.unfavorite") : Messages.Get("common.favorite"), post.ToggleFavorite),
            FolderMenu(post.Post, services),
            new Separator(),
            Item(Messages.Get("next.watchLater"), () =>
            {
                var name = Messages.Get("next.watchLater");
                services.Folders.Create(name);
                services.Folders.Add(name, post.Post);
            }),
            Item(Messages.Get("next.openPostPage"), post.OpenPage),
            Item(Messages.Get("next.openSource"), post.OpenSource, !string.IsNullOrWhiteSpace(post.Post.Source)),
            Item(Messages.Get("next.copyTags"), () => _ = post.CopyTags()),
            Item(Messages.Get("next.copyUrl"), () => _ = post.CopyFileUrl()),
        };
        if (PostActions.RelatedItem(post.Post, services) is { } related)
        {
            items.Insert(items.Count - 5, related);
        }
        items.Insert(items.Count - 5, PostActions.ReverseSearchItem(post.Post));
        if (post.Owner is FolderTabViewModel folder)
        {
            items.Add(new Separator());
            items.Add(Item(Messages.Get("folder.remove"), () => folder.RemoveFromFolder(post)));
        }
        return items;
    }

    public static MenuItem FolderMenu(Kurotsuki.Core.Booru.Post post, AppServices services)
    {
        var menu = new MenuItem { Header = Messages.Get("grid.addToFolder") };
        foreach (var name in services.Folders.Names)
        {
            menu.Items.Add(Item(name, () => services.Folders.Add(name, post)));
        }
        if (services.Folders.Names.Count > 0)
        {
            menu.Items.Add(new Separator());
        }
        menu.Items.Add(Item(Messages.Get("folders.new"), async () =>
        {
            var name = await Dialogs.PromptAsync(Shell.MainWindow, Messages.Get("folders.newTitle"), Messages.Get("folders.name"));
            if (name is not null)
            {
                services.Folders.Create(name);
                services.Folders.Add(name, post);
            }
        }));
        return menu;
    }

    /// <summary>Menu of a favourite tag in the sidebar (and of a tag in the viewer): search, groups, favourite.</summary>
    public static List<Control> ForTag(string tag, TagGroup? currentGroup, AppServices services, Action<string> search,
        Action<string>? info = null)
    {
        var items = new List<Control> { Item(Messages.Get("common.search"), () => search(tag)) };
        if (info is not null)
        {
            items.Add(Item(Messages.Get("next.tagInfo"), () => info(tag)));
        }
        var add = new MenuItem { Header = Messages.Get("tagGroups.addTo") };
        foreach (var group in services.TagGroups.Groups.Where(g => !g.Contains(tag)))
        {
            add.Items.Add(Item(group.Name, () => services.TagGroups.AddTag(group, tag)));
        }
        if (add.Items.Count > 0)
        {
            add.Items.Add(new Separator());
        }
        add.Items.Add(Item(Messages.Get("tagGroups.new"), async () =>
        {
            var name = await Dialogs.PromptAsync(Shell.MainWindow, Messages.Get("tagGroups.newTitle"), Messages.Get("tagGroups.name"));
            if (name is not null)
            {
                services.TagGroups.AddTag(services.TagGroups.Create(name), tag);
            }
        }));
        items.Add(add);
        if (currentGroup is not null)
        {
            items.Add(Item(Messages.Get("tagGroups.removeFrom"), () => services.TagGroups.RemoveTag(currentGroup, tag)));
        }
        items.Add(new Separator());
        items.Add(Item(services.Favorites.IsTagFavorite(tag) ? Messages.Get("tagPanel.unfavorite") : Messages.Get("next.addToFavorites"),
            () => services.Favorites.ToggleTag(tag)));
        return items;
    }

    public static List<Control> ForGroup(TagGroup group, AppServices services, Action<string> search) =>
    [
        Item(Messages.Get("tagGroups.search"), () => search(string.Join(' ', group.Tags)), group.Tags.Count > 0),
        Item(Messages.Get("tagGroups.rename"), async () =>
        {
            var name = await Dialogs.PromptAsync(Shell.MainWindow, Messages.Get("tagGroups.renameTitle"), Messages.Get("tagGroups.name"), group.Name);
            if (name is not null && !services.TagGroups.Rename(group, name))
            {
                await Dialogs.MessageAsync(Shell.MainWindow, Messages.Get("tagGroups.rename"), Messages.Get("tagGroups.exists", name));
            }
        }),
        Item(Messages.Get("tagGroups.delete"), async () =>
        {
            if (await Dialogs.ConfirmAsync(Shell.MainWindow, Messages.Get("tagGroups.delete"), Messages.Get("next.confirmDelete", group.Name)))
            {
                services.TagGroups.Delete(group);
            }
        }),
    ];
}
