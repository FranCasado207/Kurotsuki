package pt.yaku.favorites;

import java.sql.SQLException;
import java.util.function.Consumer;

import javafx.collections.FXCollections;
import javafx.collections.ObservableList;
import pt.yaku.booru.Post;
import pt.yaku.i18n.Messages;
import pt.yaku.storage.LibraryStore;
import lombok.Getter;

public class Favorites {

    private final LibraryStore store;
    private final Consumer<String> onError;
    @Getter
    private final ObservableList<String> tags = FXCollections.observableArrayList();
    @Getter
    private final ObservableList<Post> posts = FXCollections.observableArrayList();

    public Favorites(LibraryStore store, Consumer<String> onError) {
        this.store = store;
        this.onError = onError;
        try {
            tags.setAll(store.favoriteTags());
            posts.setAll(store.favoritePosts());
        } catch (SQLException e) {
            onError.accept(Messages.get("favorites.saveError", e.getMessage()));
        }
    }

    public boolean isTagFavorite(String tag) {
        return tags.contains(tag);
    }

    public void toggleTag(String tag) {
        try {
            if (tags.remove(tag)) {
                store.removeFavoriteTag(tag);
            } else {
                tags.add(tag);
                store.addFavoriteTag(tag);
            }
        } catch (SQLException e) {
            onError.accept(Messages.get("favorites.saveError", e.getMessage()));
        }
    }

    public boolean isPostFavorite(String key) {
        return posts.stream().anyMatch(post -> post.key().equals(key));
    }

    public void togglePost(Post post) {
        boolean wasFavorite = posts.removeIf(favorite -> favorite.key().equals(post.key()));
        if (!wasFavorite) {
            posts.add(post);
        }
        try {
            store.setFavoritePost(post, !wasFavorite);
        } catch (SQLException e) {
            onError.accept(Messages.get("favorites.saveError", e.getMessage()));
        }
    }
}
