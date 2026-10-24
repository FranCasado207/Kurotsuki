package pt.yaku.ui;

import java.util.ArrayList;
import java.util.function.Consumer;
import java.util.function.BiConsumer;
import java.util.List;

import javafx.collections.ListChangeListener;
import javafx.scene.control.Tab;
import pt.yaku.booru.Post;
import pt.yaku.library.Library;
import pt.yaku.i18n.Messages;

public class FavoritesTab extends Tab implements GridTab {

    private final ImageGrid grid;

    public FavoritesTab(Library library, BiConsumer<Post, List<Post>> onPostClicked) {
        var favorites = library.favorites();
        setText(Messages.get("favorites.tab"));

        grid = new ImageGrid(library);
        grid.setOnPostClicked(onPostClicked);
        setContent(grid.getNode());

        Runnable refresh = () -> {
            grid.showPosts(new ArrayList<>(favorites.getPosts()));
            grid.setStatus("");
            if (favorites.getPosts().isEmpty()) {
                grid.showEmptyState(Messages.get("favorites.empty"));
            }
        };
        favorites.getPosts().addListener((ListChangeListener<Post>) change -> refresh.run());
        refresh.run();
    }

    @Override
    public ImageGrid grid() {
        return grid;
    }
}
