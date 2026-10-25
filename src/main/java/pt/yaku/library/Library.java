package pt.yaku.library;

import java.sql.SQLException;
import java.util.function.Consumer;

import pt.yaku.context.ApplicationContext;
import pt.yaku.download.DownloadQueue;
import pt.yaku.download.DownloadSettings;
import pt.yaku.download.SidecarFormat;
import pt.yaku.user.config.UserConfiguration;
import pt.yaku.download.PoolArchiver;
import pt.yaku.favorites.Favorites;
import pt.yaku.favorites.TagGroups;
import pt.yaku.storage.LibraryStore;
import pt.yaku.ui.ImageLoader;

public class Library {

    private final Favorites favorites;
    private final PostFolders folders;
    private final TagGroups tagGroups;
    private final PoolReading reading;
    private final DownloadQueue queue;
    private final LibraryStore store;
    private final ImageLoader images;

    public Library(ApplicationContext context, Consumer<String> onError) {
        try {
            store = LibraryStore.open(context);
        } catch (SQLException e) {
            throw new IllegalStateException("Could not open the library database: " + e.getMessage(), e);
        }
        this.images = context.getImageLoader();
        this.favorites = new Favorites(store, onError);
        this.folders = new PostFolders(store, onError);
        this.tagGroups = new TagGroups(store, favorites, onError);
        this.reading = new PoolReading(store, onError);
        this.queue = new DownloadQueue(context.getDownloader(), new PoolArchiver(context), () -> {
            UserConfiguration config = context.getUserConfig();
            return new DownloadSettings(config.getDownloadFolder(), config.getDownloadTemplate(),
                    SidecarFormat.parse(config.getDownloadSidecar()), !config.isRedownloadKnown());
        }, tagGroups::groupFor, store, onError);
    }

    public ImageLoader images() {
        return images;
    }

    public Favorites favorites() {
        return favorites;
    }

    public PoolReading reading() {
        return reading;
    }

    public TagGroups tagGroups() {
        return tagGroups;
    }

    public PostFolders folders() {
        return folders;
    }

    public LibraryStore store() {
        return store;
    }

    public DownloadQueue queue() {
        return queue;
    }
}
