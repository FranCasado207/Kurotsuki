using System.Collections.ObjectModel;
using Kurotsuki.Core.Library;

namespace Kurotsuki.App.ViewModels;

/// <summary>A favourite tag, with the group whose section shows it (null for the "no group" section).</summary>
public sealed record FavoriteTagItem(string Tag, TagGroup? Group)
{
    public string Display => Tag.Replace('_', ' ');
}

/// <summary>A section of the favourite-tags list: one tag group, or the tags that are in no group.</summary>
public sealed class FavoriteSection(string header, TagGroup? group, IEnumerable<string> tags)
{
    public string Header { get; } = header;
    /// <summary>Null for the "no group" section.</summary>
    public TagGroup? Group { get; } = group;
    public ObservableCollection<FavoriteTagItem> Tags { get; } = new(tags.Select(t => new FavoriteTagItem(t, group)));
    public bool HasHeader => Header.Length > 0;
}
