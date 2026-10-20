package pt.yaku.library;

import javafx.collections.FXCollections;
import javafx.collections.ObservableList;
import lombok.Getter;
import pt.yaku.booru.Post;

public class PostFolder {

    @Getter
    private final String name;
    @Getter
    private final ObservableList<Post> posts;

    public PostFolder(String name, java.util.List<Post> posts) {
        this.name = name;
        this.posts = FXCollections.observableArrayList(posts);
    }

    public boolean contains(String key) {
        return posts.stream().anyMatch(post -> post.key().equals(key));
    }

    @Override
    public String toString() {
        return name;
    }
}
