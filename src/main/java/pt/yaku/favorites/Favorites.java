package pt.yaku.favorites;

import java.io.IOException;
import java.util.ArrayList;
import java.util.List;
import java.util.Objects;
import java.util.function.Consumer;

import javafx.collections.FXCollections;
import javafx.collections.ObservableList;
import pt.yaku.booru.Post;
import pt.yaku.context.ApplicationContext;
import pt.yaku.i18n.Messages;
import lombok.Getter;

public class Favorites {

    private final ApplicationContext context;
    private final Consumer<String> onError;
    @Getter
    private final ObservableList<String> tags;
    @Getter
    private final ObservableList<Post> posts;

    public Favorites(ApplicationContext context, Consumer<String> onError) {
        this.context = context;
        this.onError = onError;
        tags = FXCollections.observableArrayList(
                Objects.requireNonNullElse(context.getUserConfig().getFavoriteTags(), List.of()));
        posts = FXCollections.observableArrayList(
                Objects.requireNonNullElse(context.getUserConfig().getFavoritePosts(), List.of()));
    }



    public boolean isTagFavorite(String tag) {
        return tags.contains(tag);
    }

    public void toggleTag(String tag) {
        if (!tags.remove(tag)) {
            tags.add(tag);
        }
        persist();
    }

    public boolean isPostFavorite(String key) {
        return posts.stream().anyMatch(post -> post.key().equals(key));
    }

    public void togglePost(Post post) {
        if (!posts.removeIf(favorite -> favorite.key().equals(post.key()))) {
            posts.add(post);
        }
        persist();
    }

    private void persist() {
        context.getUserConfig().setFavoriteTags(new ArrayList<>(tags));
        context.getUserConfig().setFavoritePosts(new ArrayList<>(posts));
        try {
            context.saveUserConfig();
        } catch (IOException e) {
            onError.accept(Messages.get("favorites.saveError", e.getMessage()));
        }
    }
}
