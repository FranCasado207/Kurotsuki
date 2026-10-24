package pt.yaku.library;

import java.sql.SQLException;
import java.util.function.Consumer;

import pt.yaku.context.ApplicationContext;
import pt.yaku.download.DownloadQueue;
import pt.yaku.favorites.Favorites;
import pt.yaku.storage.LibraryStore;

public class Library {

    private final Favorites favorites;
    private final PostFolders folders;
    private final DownloadQueue queue;
    private final LibraryStore store;

    public Library(ApplicationContext context, Consumer<String> onError) {
        try {
            store = LibraryStore.open(context);
        } catch (SQLException e) {
            throw new IllegalStateException("Could not open the library database: " + e.getMessage(), e);
        }
        this.favorites = new Favorites(store, onError);
        this.folders = new PostFolders(store, onError);
        this.queue = new DownloadQueue(() -> context.getUserConfig().getDownloadFolder(), onError);
    }

    public Favorites favorites() {
        return favorites;
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
