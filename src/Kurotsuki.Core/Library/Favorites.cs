using System.Collections.ObjectModel;
using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Storage;

namespace Kurotsuki.Core.Library;

/// <summary>Favourite tags and favourite posts, backed by the library database.</summary>
public sealed class Favorites
{
    private readonly LibraryStore _store;
    private readonly Action<string> _onError;
    private readonly HashSet<string> _postKeys;

    public Favorites(LibraryStore store, Action<string> onError)
    {
        _store = store;
        _onError = onError;
        try
        {
            Tags = new ObservableCollection<string>(store.FavoriteTags());
            _postKeys = store.FavoritePostKeys();
        }
        catch (Exception e)
        {
            onError(I18n.Messages.Get("favorites.saveError", e.Message));
            Tags = [];
            _postKeys = [];
        }
    }

    public ObservableCollection<string> Tags { get; }

    /// <summary>Raised after posts were (un)favourited, so open grids can refresh a badge.</summary>
    public event Action? PostsChanged;

    /// <summary>Re-reads everything from the database (after a backup import).</summary>
    public void Reload()
    {
        try
        {
            var tags = _store.FavoriteTags();
            Tags.Clear();
            foreach (var tag in tags)
            {
                Tags.Add(tag);
            }
            _postKeys.Clear();
            _postKeys.UnionWith(_store.FavoritePostKeys());
        }
        catch (Exception e)
        {
            _onError(I18n.Messages.Get("favorites.saveError", e.Message));
        }
        PostsChanged?.Invoke();
    }

    public bool IsTagFavorite(string tag) => Tags.Contains(tag);
    public bool IsPostFavorite(Post post) => _postKeys.Contains(post.Key);

    public void ToggleTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return;
        }
        var index = Tags.IndexOf(tag);
        if (index >= 0)
        {
            Tags.RemoveAt(index);
            StoreWrites.Persist("favorites.saveError", _onError, () => _store.RemoveFavoriteTag(tag),
                () => Tags.Insert(Math.Min(index, Tags.Count), tag));
        }
        else
        {
            Tags.Add(tag);
            StoreWrites.Persist("favorites.saveError", _onError, () => _store.AddFavoriteTag(tag), () => Tags.Remove(tag));
        }
    }

    public void TogglePost(Post post)
    {
        var add = _postKeys.Add(post.Key);
        if (!add)
        {
            _postKeys.Remove(post.Key);
        }
        StoreWrites.Persist("favorites.saveError", _onError, () => _store.SetFavoritePost(post, add), () =>
        {
            if (add) _postKeys.Remove(post.Key); else _postKeys.Add(post.Key);
        });
        PostsChanged?.Invoke();
    }

    /// <summary>Favourite posts, oldest first.</summary>
    public List<Post> LoadPosts()
    {
        try
        {
            return _store.FavoritePosts();
        }
        catch (Exception e)
        {
            _onError(I18n.Messages.Get("favorites.saveError", e.Message));
            return [];
        }
    }
}
