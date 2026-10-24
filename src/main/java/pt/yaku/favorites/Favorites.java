package pt.yaku.favorites;

import java.sql.SQLException;
import java.util.HashSet;
import java.util.Set;
import java.util.function.Consumer;

import javafx.collections.FXCollections;
import javafx.collections.ListChangeListener;
import javafx.collections.ObservableList;
import pt.yaku.booru.Post;
import pt.yaku.i18n.Messages;
import pt.yaku.storage.LibraryStore;
import pt.yaku.storage.StoreWrites;
import lombok.Getter;

public class Favorites {

    private final LibraryStore store;
    private final Consumer<String> onError;
    @Getter
    private final ObservableList<String> tags = FXCollections.observableArrayList();
    @Getter
    private final ObservableList<Post> posts = FXCollections.observableArrayList();
    // Lookups happen per grid cell / tag chip, so keep them O(1) instead of scanning the lists.
    private Set<String> tagSet = Set.of();
    private Set<String> postKeys = Set.of();

    public Favorites(LibraryStore store, Consumer<String> onError) {
        this.store = store;
        this.onError = onError;
        tags.addListener((ListChangeListener<String>) change -> tagSet = new HashSet<>(tags));
        posts.addListener((ListChangeListener<Post>) change -> {
            Set<String> keys = new HashSet<>();
            posts.forEach(post -> keys.add(post.key()));
            postKeys = keys;
        });
        try {
            tags.setAll(store.favoriteTags());
            posts.setAll(store.favoritePosts());
        } catch (SQLException e) {
            onError.accept(Messages.get("favorites.saveError", e.getMessage()));
        }
    }

    public boolean isTagFavorite(String tag) {
        return tagSet.contains(tag);
    }

    public void toggleTag(String tag) {
        if (tags.remove(tag)) {
            persist(() -> store.removeFavoriteTag(tag), () -> tags.add(tag));
        } else {
            tags.add(tag);
            persist(() -> store.addFavoriteTag(tag), () -> tags.remove(tag));
        }
    }

    public boolean isPostFavorite(String key) {
        return postKeys.contains(key);
    }

    public void togglePost(Post post) {
        boolean wasFavorite = posts.removeIf(favorite -> favorite.key().equals(post.key()));
        if (!wasFavorite) {
            posts.add(post);
        }
        persist(() -> store.setFavoritePost(post, !wasFavorite), () -> {
            posts.removeIf(favorite -> favorite.key().equals(post.key()));
            if (wasFavorite) {
                posts.add(post);
            }
        });
    }

    private void persist(LibraryStore.SqlTask write, Runnable undo) {
        StoreWrites.persist(store, "favorites.saveError", onError, write, undo);
    }
}
