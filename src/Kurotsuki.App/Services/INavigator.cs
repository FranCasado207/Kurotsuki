using Kurotsuki.Core.Booru;
using Kurotsuki.Core.Config;

namespace Kurotsuki.App.Services;

/// <summary>What view models may ask of the shell: open tabs and windows. Implemented by the main view model.</summary>
public interface INavigator
{
    void OpenSearch(BooruConfiguration booru, string? tags);
    void OpenPool(Pool pool, BooruConfiguration booru);
    void OpenViewer(IReadOnlyList<Post> posts, int index);
    void OpenReader(Pool pool, BooruConfiguration booru, int startPage);
    void OpenFavorites();
    /// <summary>Shows what a grid hid because of the blacklist, so the user can check it and bring posts back.</summary>
    void OpenBlacklistReport(ViewModels.PostGridViewModel grid);
    void OpenFolders();
    void OpenFolder(string name);
    void OpenPools(BooruConfiguration? booru = null);
    void OpenReading();
    /// <summary>Opens the search tab on the booru the user has selected in the sidebar.</summary>
    void SearchSelected(string? tags);
}
