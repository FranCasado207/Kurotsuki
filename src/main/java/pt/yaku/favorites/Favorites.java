package pt.yaku.favorites;

import java.io.IOException;
import java.util.ArrayList;
import java.util.List;
import java.util.Objects;
import java.util.function.Consumer;

import javafx.collections.FXCollections;
import javafx.collections.ObservableList;
import pt.yaku.client.GelbooruPost;
import pt.yaku.context.ApplicationContext;
import pt.yaku.i18n.Messages;

public class Favorites {

    private final ApplicationContext context;
    private final Consumer<String> onError;
    private final ObservableList<String> tags;
    private final ObservableList<GelbooruPost> posts;

    public Favorites(ApplicationContext context, Consumer<String> onError) {
        this.context = context;
        this.onError = onError;
        tags = FXCollections.observableArrayList(
                Objects.requireNonNullElse(context.getUserConfig().getFavoriteTags(), List.of()));
        posts = FXCollections.observableArrayList(
                Objects.requireNonNullElse(context.getUserConfig().getFavoritePosts(), List.of()));
    }

    public ObservableList<String> getTags() {
        return tags;
    }

    public ObservableList<GelbooruPost> getPosts() {
        return posts;
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

    public boolean isPostFavorite(long id) {
        return posts.stream().anyMatch(post -> post.getId() == id);
    }

    public void togglePost(GelbooruPost post) {
        if (!posts.removeIf(favorite -> favorite.getId() == post.getId())) {
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
