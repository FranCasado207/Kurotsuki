using System.Collections.ObjectModel;
using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Storage;

namespace Kurotsuki.Core.Library;

/// <summary>User folders of saved posts (config <c>postFolders</c> in old versions; now the <c>folders</c> tables).</summary>
public sealed class PostFolders
{
    private readonly LibraryStore _store;
    private readonly Action<string> _onError;

    public PostFolders(LibraryStore store, Action<string> onError)
    {
        _store = store;
        _onError = onError;
        Reload();
    }

    public ObservableCollection<string> Names { get; } = [];

    public void Reload()
    {
        try
        {
            Names.Clear();
            foreach (var name in _store.Folders().Keys)
            {
                Names.Add(name);
            }
        }
        catch (Exception e)
        {
            _onError(I18n.Messages.Get("folders.saveError", e.Message));
        }
    }

    public List<Post> PostsOf(string folder)
    {
        try
        {
            return _store.Folders().GetValueOrDefault(folder) ?? [];
        }
        catch (Exception e)
        {
            _onError(I18n.Messages.Get("folders.saveError", e.Message));
            return [];
        }
    }

    public bool Create(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0 || Names.Contains(trimmed))
        {
            return false;
        }
        Names.Add(trimmed);
        StoreWrites.Persist("folders.saveError", _onError, () => _store.CreateFolder(trimmed), () => Names.Remove(trimmed));
        return true;
    }

    public void Delete(string name)
    {
        var index = Names.IndexOf(name);
        if (index < 0)
        {
            return;
        }
        Names.RemoveAt(index);
        StoreWrites.Persist("folders.saveError", _onError, () => _store.DeleteFolder(name),
            () => Names.Insert(Math.Min(index, Names.Count), name));
    }

    public void Add(string folder, Post post) =>
        StoreWrites.Persist("folders.saveError", _onError, () => _store.AddToFolder(folder, post), () => { });

    public void Remove(string folder, Post post) =>
        StoreWrites.Persist("folders.saveError", _onError, () => _store.RemoveFromFolder(folder, post), () => { });
}
