using System.Collections.ObjectModel;
using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Storage;

namespace Kurotsuki.Core.Library;

public sealed class TagGroup(string name, IEnumerable<string> tags)
{
    public string Name { get; } = name;
    public ObservableCollection<string> Tags { get; } = new(tags);
    public bool Contains(string tag) => Tags.Contains(tag);
    public override string ToString() => Name;
}

/// <summary>
/// Named groups of favourite tags, to keep a long favourites list organised. A tag can be in several groups; a tag
/// that stops being a favourite leaves all of them.
/// </summary>
public sealed class TagGroups
{
    private readonly LibraryStore _store;
    private readonly Favorites _favorites;
    private readonly Action<string> _onError;

    public TagGroups(LibraryStore store, Favorites favorites, Action<string> onError)
    {
        _store = store;
        _favorites = favorites;
        _onError = onError;
        try
        {
            foreach (var (name, tags) in store.TagGroups())
            {
                Groups.Add(new TagGroup(name, tags));
            }
        }
        catch (Exception e)
        {
            onError(I18n.Messages.Get("tagGroups.saveError", e.Message));
        }
        // The database drops the membership through a foreign key; this keeps the in-memory lists in step.
        favorites.Tags.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null)
            {
                foreach (var group in Groups)
                {
                    foreach (string removed in e.OldItems)
                    {
                        group.Tags.Remove(removed);
                    }
                }
            }
        };
    }

    public ObservableCollection<TagGroup> Groups { get; } = [];

    /// <summary>Re-reads the groups from the database (after a backup import).</summary>
    public void Reload()
    {
        try
        {
            var loaded = _store.TagGroups().Select(g => new TagGroup(g.Key, g.Value)).ToList();
            Groups.Clear();
            foreach (var group in loaded)
            {
                Groups.Add(group);
            }
        }
        catch (Exception e)
        {
            _onError(I18n.Messages.Get("tagGroups.saveError", e.Message));
        }
    }

    /// <summary>The name of the first group (in list order) that holds any of the post's tags, or an empty string.</summary>
    public string GroupFor(Post post) => FirstGroup(Groups, post.Tags);

    internal static string FirstGroup(IEnumerable<TagGroup> groups, string? tagString)
    {
        if (string.IsNullOrWhiteSpace(tagString))
        {
            return "";
        }
        var tags = new HashSet<string>(tagString.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return groups.FirstOrDefault(g => g.Tags.Any(tags.Contains))?.Name ?? "";
    }

    public bool Exists(string name) => Find(name) is not null;
    public TagGroup? Find(string name) => Groups.FirstOrDefault(g => g.Name == name);
    public bool IsGrouped(string tag) => Groups.Any(g => g.Contains(tag));

    public TagGroup Create(string name)
    {
        var trimmed = name.Trim();
        if (Find(trimmed) is { } existing)
        {
            return existing;
        }
        var group = new TagGroup(trimmed, []);
        Groups.Add(group);
        Persist(() => _store.CreateTagGroup(trimmed), () => Groups.Remove(group));
        return group;
    }

    /// <summary>Returns false (and changes nothing) if another group already uses <paramref name="newName"/>.</summary>
    public bool Rename(TagGroup group, string newName)
    {
        var trimmed = newName.Trim();
        var index = Groups.IndexOf(group);
        if (index < 0 || trimmed.Length == 0 || (Find(trimmed) is not null && trimmed != group.Name))
        {
            return false;
        }
        if (trimmed == group.Name)
        {
            return true;
        }
        var renamed = new TagGroup(trimmed, group.Tags);
        Groups[index] = renamed;
        Persist(() => _store.RenameTagGroup(group.Name, trimmed), () =>
        {
            var current = Groups.IndexOf(renamed);
            if (current >= 0)
            {
                Groups[current] = group;
            }
        });
        return true;
    }

    public void Delete(TagGroup group)
    {
        var index = Groups.IndexOf(group);
        Groups.Remove(group);
        Persist(() => _store.DeleteTagGroup(group.Name), () =>
        {
            if (!Groups.Contains(group))
            {
                Groups.Insert(Math.Clamp(index, 0, Groups.Count), group);
            }
        });
    }

    /// <summary>Adds the tag to the group, making it a favourite first if it is not one yet.</summary>
    public void AddTag(TagGroup group, string tag)
    {
        if (group.Contains(tag))
        {
            return;
        }
        if (!_favorites.IsTagFavorite(tag))
        {
            _favorites.ToggleTag(tag);
        }
        group.Tags.Add(tag);
        Persist(() => _store.AddTagToGroup(group.Name, tag), () => group.Tags.Remove(tag));
    }

    public void RemoveTag(TagGroup group, string tag)
    {
        var index = group.Tags.IndexOf(tag);
        if (group.Tags.Remove(tag))
        {
            Persist(() => _store.RemoveTagFromGroup(group.Name, tag), () => group.Tags.Insert(Math.Clamp(index, 0, group.Tags.Count), tag));
        }
    }

    private void Persist(Action write, Action undo) => StoreWrites.Persist("tagGroups.saveError", _onError, write, undo);
}
