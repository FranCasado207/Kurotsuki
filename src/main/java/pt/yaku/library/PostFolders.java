package pt.yaku.library;

import java.sql.SQLException;
import java.util.List;
import java.util.Map;
import java.util.function.Consumer;

import javafx.collections.FXCollections;
import javafx.collections.ObservableList;
import pt.yaku.booru.Post;
import pt.yaku.i18n.Messages;
import pt.yaku.storage.LibraryStore;
import lombok.Getter;

public class PostFolders {

    private final LibraryStore store;
    private final Consumer<String> onError;
    @Getter
    private final ObservableList<PostFolder> folders = FXCollections.observableArrayList();

    public PostFolders(LibraryStore store, Consumer<String> onError) {
        this.store = store;
        this.onError = onError;
        try {
            for (Map.Entry<String, List<Post>> folder : store.folders().entrySet()) {
                folders.add(new PostFolder(folder.getKey(), folder.getValue()));
            }
        } catch (SQLException e) {
            onError.accept(Messages.get("folders.saveError", e.getMessage()));
        }
    }

    public PostFolder create(String name) {
        String trimmed = name.trim();
        for (PostFolder folder : folders) {
            if (folder.getName().equals(trimmed)) {
                return folder;
            }
        }
        PostFolder folder = new PostFolder(trimmed, List.of());
        folders.add(folder);
        try {
            store.createFolder(trimmed);
        } catch (SQLException e) {
            onError.accept(Messages.get("folders.saveError", e.getMessage()));
        }
        return folder;
    }

    public void delete(PostFolder folder) {
        folders.remove(folder);
        try {
            store.deleteFolder(folder.getName());
        } catch (SQLException e) {
            onError.accept(Messages.get("folders.saveError", e.getMessage()));
        }
    }

    public void addPost(PostFolder folder, Post post) {
        if (!folder.contains(post.key())) {
            folder.getPosts().add(post);
            try {
                store.addToFolder(folder.getName(), post);
            } catch (SQLException e) {
                onError.accept(Messages.get("folders.saveError", e.getMessage()));
            }
        }
    }

    public void removePost(PostFolder folder, Post post) {
        if (folder.getPosts().removeIf(item -> item.key().equals(post.key()))) {
            try {
                store.removeFromFolder(folder.getName(), post);
            } catch (SQLException e) {
                onError.accept(Messages.get("folders.saveError", e.getMessage()));
            }
        }
    }
}
