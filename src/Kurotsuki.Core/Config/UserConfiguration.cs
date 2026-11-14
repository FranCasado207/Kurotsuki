using System.Text.Json;
using System.Text.Json.Serialization;
using Kurotsuki.Core.Booru;

namespace Kurotsuki.Core.Config;

public sealed class FolderData
{
    public string? Name { get; set; }
    public List<Post>? Posts { get; set; }
}

/// <summary>
/// The contents of <c>config.json</c>, field for field the same as the JavaFX app's UserConfiguration.
/// Fields this base does not use yet (panels, reader, folders...) are still declared so they survive a save.
/// </summary>
public sealed class UserConfiguration
{
    public const int DefaultReaderCacheMb = 256;
    public const int MinReaderCacheMb = 32;

    public List<BooruConfiguration> BooruConfigurations { get; set; } = [];
    public string? DownloadFolder { get; set; }
    public string? CacheFolder { get; set; }
    public string? Language { get; set; }
    public Dictionary<string, string> PanelDocks { get; set; } = [];
    public Dictionary<string, double> PanelSizes { get; set; } = [];
    public List<string> FavoriteTags { get; set; } = [];
    public List<Post> FavoritePosts { get; set; } = [];
    public List<FolderData> PostFolders { get; set; } = [];
    public List<string> BlacklistedTags { get; set; } = [];
    /// <summary>cbz (default), zip, cbt, tar, cbr, rar, cb7 or 7z.</summary>
    public string? PoolArchiveFormat { get; set; }
    public string? ReaderMode { get; set; }
    public string? ReaderFit { get; set; }
    public bool ReaderRightToLeft { get; set; }
    public bool ReaderSpread { get; set; }
    public int ReaderCacheMegabytes { get; set; }
    public bool ReaderHonorBlacklist { get; set; }
    /// <summary>Path template for saved files, e.g. <c>%booru%/%artist%/%id%_%md5%.%ext%</c>; empty means the file name.</summary>
    public string? DownloadTemplate { get; set; }
    /// <summary>Metadata file next to each download: none (default), txt or json.</summary>
    public string? DownloadSidecar { get; set; }
    /// <summary>When true, posts saved before are downloaded again instead of skipped.</summary>
    public bool RedownloadKnown { get; set; }

    /// <summary>Anything the other app (or a newer version) wrote that this one does not know about.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    public int EffectiveReaderCacheMegabytes =>
        ReaderCacheMegabytes <= 0 ? DefaultReaderCacheMb : Math.Max(MinReaderCacheMb, ReaderCacheMegabytes);
}
