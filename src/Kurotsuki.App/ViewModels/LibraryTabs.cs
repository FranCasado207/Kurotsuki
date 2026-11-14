using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kurotsuki.App.Services;
using Kurotsuki.Core.I18n;

namespace Kurotsuki.App.ViewModels;

/// <summary>The favourite posts, oldest first. Refreshes when a post is (un)favourited elsewhere.</summary>
public sealed class FavoritesTabViewModel : PostGridViewModel
{
    public FavoritesTabViewModel(AppServices services) : base(services)
    {
        Title = Messages.Get("favorites.tab");
        EmptyText = Messages.Get("favorites.empty");
        services.Favorites.PostsChanged += Reload;
        Reload();
    }

    private void Reload()
    {
        // Keep the posts that are still favourites (and their loaded thumbnails); add the new ones.
        var wanted = Services.Favorites.LoadPosts();
        var keep = wanted.Select(p => p.Key).ToHashSet();
        foreach (var gone in Posts.Where(p => !keep.Contains(p.Post.Key)).ToList())
        {
            Posts.Remove(gone);
        }
        var have = Posts.Select(p => p.Post.Key).ToHashSet();
        AddPosts(wanted.Where(p => !have.Contains(p.Key)));
        ShowEmpty = Posts.Count == 0;
    }

    public override void Dispose()
    {
        Services.Favorites.PostsChanged -= Reload;
        base.Dispose();
    }
}

public sealed class FolderTabViewModel : PostGridViewModel
{
    public FolderTabViewModel(AppServices services, string folder) : base(services)
    {
        Folder = folder;
        Title = folder;
        EmptyText = Messages.Get("folder.empty");
        Reload();
    }

    public string Folder { get; }

    public void Reload()
    {
        ClearPosts();
        AddPosts(Services.Folders.PostsOf(Folder));
        ShowEmpty = Posts.Count == 0;
    }

    public void RemoveFromFolder(PostViewModel post)
    {
        Services.Folders.Remove(Folder, post.Post);
        Posts.Remove(post);
        ShowEmpty = Posts.Count == 0;
    }
}

/// <summary>The list of folders: open, create, delete.</summary>
public sealed partial class FoldersTabViewModel : TabViewModel
{
    private readonly AppServices _services;

    public FoldersTabViewModel(AppServices services)
    {
        _services = services;
        Title = Messages.Get("folders.tab");
    }

    public ObservableCollection<string> Folders => _services.Folders.Names;

    [ObservableProperty] private string? _selected;

    [RelayCommand]
    private async Task NewFolder()
    {
        var name = await Dialogs.PromptAsync(Shell.MainWindow, Messages.Get("folders.newTitle"), Messages.Get("folders.name"));
        if (name is not null)
        {
            _services.Folders.Create(name);
        }
    }

    [RelayCommand]
    private void Open(string? folder)
    {
        if (folder is not null)
        {
            _services.Navigator.OpenFolder(folder);
        }
    }

    [RelayCommand]
    private async Task Delete(string? folder)
    {
        if (folder is not null && await Dialogs.ConfirmAsync(Shell.MainWindow, Messages.Get("folders.delete"),
                Messages.Get("next.confirmDelete", folder)))
        {
            _services.Folders.Delete(folder);
        }
    }
}
