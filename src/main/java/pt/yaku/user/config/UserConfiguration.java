package pt.yaku.user.config;

import lombok.Getter;
import lombok.NoArgsConstructor;
import lombok.Setter;
import pt.yaku.booru.Post;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

@Getter
@Setter
@NoArgsConstructor
public class UserConfiguration {
    List<BooruConfiguration> booruConfigurations = new ArrayList<>();
    String downloadFolder;
    String cacheFolder;
    String language;
    Map<String, String> panelDocks = new HashMap<>();
    Map<String, Double> panelSizes = new HashMap<>();
    List<String> favoriteTags = new ArrayList<>();
    List<Post> favoritePosts = new ArrayList<>();
    List<FolderData> postFolders = new ArrayList<>();
    List<String> blacklistedTags = new ArrayList<>();
    /** Extension pools are saved as: cbz (default), zip, cbt, tar, cbr, rar, cb7 or 7z. */
    String poolArchiveFormat;
    String readerMode;
    String readerFit;
    boolean readerRightToLeft;
    boolean readerSpread;
    /** Memory the reader may use for decoded pages; 0 or less means the default. */
    int readerCacheMegabytes;
    /** When true the reader leaves out pages whose tags are on the blacklist. */
    boolean readerHonorBlacklist;

    /** Path template for saved files, e.g. {@code %booru%/%artist%/%id%_%md5%.%ext%}; empty means the file name. */
    String downloadTemplate;
    /** Metadata file written next to each download: none (default), txt or json. */
    String downloadSidecar;
    /** When true, posts that were saved before are downloaded again instead of skipped. */
    boolean redownloadKnown;

    public static final int DEFAULT_READER_CACHE_MB = 256;
    public static final int MIN_READER_CACHE_MB = 32;

    public int effectiveReaderCacheMegabytes() {
        return readerCacheMegabytes <= 0 ? DEFAULT_READER_CACHE_MB
                : Math.max(MIN_READER_CACHE_MB, readerCacheMegabytes);
    }

}
