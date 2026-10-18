package pt.yaku.ui;

import java.util.ArrayList;
import java.util.function.Consumer;

import javafx.collections.ListChangeListener;
import javafx.scene.control.Tab;
import pt.yaku.booru.Post;
import pt.yaku.library.Library;
import pt.yaku.i18n.Messages;

public class FavoritesTab extends Tab {

    public FavoritesTab(Library library, Consumer<Post> onPostClicked) {
        var favorites = library.favorites();
        setText(Messages.get("favorites.tab"));

        ImageGrid grid = new ImageGrid(library);
        grid.setOnPostClicked(onPostClicked);
        setContent(grid.getNode());

        Runnable refresh = () -> {
            grid.showPosts(new ArrayList<>(favorites.getPosts()));
            grid.setStatus(Messages.get("favorites.count", String.valueOf(favorites.getPosts().size())));
        };
        favorites.getPosts().addListener((ListChangeListener<Post>) change -> refresh.run());
        refresh.run();
    }
}
