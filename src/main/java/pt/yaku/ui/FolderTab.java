package pt.yaku.ui;

import java.util.ArrayList;
import java.util.function.Consumer;
import java.util.function.BiConsumer;
import java.util.List;

import javafx.collections.ListChangeListener;
import javafx.collections.WeakListChangeListener;
import javafx.scene.control.Tab;
import pt.yaku.booru.Post;
import pt.yaku.library.Library;
import pt.yaku.library.PostFolder;
import pt.yaku.i18n.Messages;

public class FolderTab extends Tab implements GridTab {

    private final ImageGrid grid;

    private final ListChangeListener<Post> folderListener;

    public FolderTab(Library library, PostFolder folder, BiConsumer<Post, List<Post>> onPostClicked) {
        setText(folder.getName());

        grid = new ImageGrid(library);
        grid.setOnPostClicked(onPostClicked);
        grid.setRemoveAction(Messages.get("folder.remove"), post -> library.folders().removePost(folder, post));
        setContent(grid.getNode());

        folderListener = change -> {
            grid.showPosts(new ArrayList<>(folder.getPosts()));
            grid.setStatus("");
            if (folder.getPosts().isEmpty()) {
                grid.showEmptyState(Messages.get("folder.empty"));
            }
        };
        folder.getPosts().addListener(new WeakListChangeListener<>(folderListener));
        folderListener.onChanged(null);
    }

    @Override
    public ImageGrid grid() {
        return grid;
    }
}
