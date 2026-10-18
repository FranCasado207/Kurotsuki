package pt.yaku.library;

import java.util.function.Consumer;

import pt.yaku.context.ApplicationContext;
import pt.yaku.download.DownloadQueue;
import pt.yaku.favorites.Favorites;

public class Library {

    private final Favorites favorites;
    private final PostFolders folders;
    private final DownloadQueue queue;

    public Library(ApplicationContext context, Consumer<String> onError) {
        this.favorites = new Favorites(context, onError);
        this.folders = new PostFolders(context, onError);
        this.queue = new DownloadQueue(() -> context.getUserConfig().getDownloadFolder(), onError);
    }

    public Favorites favorites() {
        return favorites;
    }

    public PostFolders folders() {
        return folders;
    }

    public DownloadQueue queue() {
        return queue;
    }
}
