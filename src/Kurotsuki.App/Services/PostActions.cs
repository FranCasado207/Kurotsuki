using Avalonia.Controls;
using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Config;
using Kurotsuki.Core.I18n;

namespace Kurotsuki.App.Services;

/// <summary>Menu entries about a post or a tag that more than one window offers: related posts, reverse image search, tag info.</summary>
public static class PostActions
{
    /// <summary>"Parent / children" for a post that has any; searches on the booru the post came from.</summary>
    public static MenuItem? RelatedItem(Post post, AppServices services)
    {
        if (post.RelatedQuery is not { } query || services.FindBooru(post.BooruBaseUrl) is not { } booru)
        {
            return null;
        }
        var item = new MenuItem { Header = Messages.Get("next.relatedPosts") };
        item.Click += (_, _) => services.Navigator.OpenSearch(booru, query);
        return item;
    }

    public static MenuItem ReverseSearchItem(Post post)
    {
        var url = ReverseImageSearch.ImageUrl(post);
        var menu = new MenuItem { Header = Messages.Get("next.searchByImage"), IsEnabled = url is not null };
        foreach (var (name, build) in ReverseImageSearch.Engines)
        {
            var item = new MenuItem { Header = name };
            item.Click += (_, _) => Shell.OpenUrl(build(url!));
            menu.Items.Add(item);
        }
        return menu;
    }

    /// <summary>Fetches the tag's wiki, aliases and implications from the post's booru and shows them in a window.</summary>
    public static async Task ShowTagInfoAsync(string tag, BooruConfiguration? booru, AppServices services, Window? owner)
    {
        var title = Messages.Get("next.tagInfoTitle", tag);
        if (booru is null)
        {
            await Dialogs.MessageAsync(owner, title, Messages.Get("next.tagInfoNone"));
            return;
        }
        TagInfo? info;
        try
        {
            info = await services.Clients.ClientFor(booru).TagInfoAsync(tag);
        }
        catch (Exception e)
        {
            await Dialogs.MessageAsync(owner, title, Messages.Get("next.tagInfoError", e.Message));
            return;
        }
        if (info is null)
        {
            await Dialogs.MessageAsync(owner, title, Messages.Get("next.tagInfoNone"));
            return;
        }
        await Dialogs.ShowTextAsync(owner, title, Describe(info));
    }

    public static string Describe(TagInfo info)
    {
        var parts = new List<string>();
        if (info.PlainWiki is { Length: > 0 } wiki)
        {
            parts.Add(wiki);
        }
        void List(string key, IReadOnlyList<string> tags)
        {
            if (tags.Count > 0)
            {
                parts.Add(Messages.Get(key) + "\n" + string.Join(", ", tags.Select(t => t.Replace('_', ' '))));
            }
        }
        List("next.tagAliasOf", info.AliasOf);
        List("next.tagAliases", info.Aliases);
        List("next.tagImplies", info.Implies);
        List("next.tagImpliedBy", info.ImpliedBy);
        return string.Join("\n\n", parts);
    }
}
