package pt.yaku.library;

import javafx.collections.FXCollections;
import javafx.collections.ObservableList;
import pt.yaku.client.GelbooruPost;

public class PostFolder {

    private final String name;
    private final ObservableList<GelbooruPost> posts;

    public PostFolder(String name, java.util.List<GelbooruPost> posts) {
        this.name = name;
        this.posts = FXCollections.observableArrayList(posts);
    }

    public String getName() {
        return name;
    }

    public ObservableList<GelbooruPost> getPosts() {
        return posts;
    }

    public boolean contains(long postId) {
        return posts.stream().anyMatch(post -> post.getId() == postId);
    }

    @Override
    public String toString() {
        return name;
    }
}
