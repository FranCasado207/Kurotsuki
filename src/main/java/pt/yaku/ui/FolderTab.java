package pt.yaku.ui;

import java.util.ArrayList;
import java.util.function.Consumer;

import javafx.collections.ListChangeListener;
import javafx.collections.WeakListChangeListener;
import javafx.scene.control.Tab;
import pt.yaku.client.GelbooruPost;
import pt.yaku.library.Library;
import pt.yaku.library.PostFolder;
import pt.yaku.i18n.Messages;

public class FolderTab extends Tab {

    private final ListChangeListener<GelbooruPost> folderListener;

    public FolderTab(Library library, PostFolder folder, Consumer<GelbooruPost> onPostClicked) {
        setText(folder.getName());

        ImageGrid grid = new ImageGrid(library);
        grid.setOnPostClicked(onPostClicked);
        grid.setRemoveAction(Messages.get("folder.remove"), post -> library.folders().removePost(folder, post));
        setContent(grid.getNode());

        folderListener = change -> {
            grid.showPosts(new ArrayList<>(folder.getPosts()));
            grid.setStatus(Messages.get("folder.count", String.valueOf(folder.getPosts().size())));
        };
        folder.getPosts().addListener(new WeakListChangeListener<>(folderListener));
        folderListener.onChanged(null);
    }
}
