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
import pt.yaku.storage.StoreWrites;
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
        persist(() -> store.createFolder(trimmed), () -> folders.remove(folder));
        return folder;
    }

    public void delete(PostFolder folder) {
        int index = folders.indexOf(folder);
        folders.remove(folder);
        persist(() -> store.deleteFolder(folder.getName()), () -> {
            if (!folders.contains(folder)) {
                folders.add(Math.min(Math.max(index, 0), folders.size()), folder);
            }
        });
    }

    public void addPost(PostFolder folder, Post post) {
        if (!folder.contains(post.key())) {
            folder.getPosts().add(post);
            persist(() -> store.addToFolder(folder.getName(), post),
                    () -> folder.getPosts().removeIf(item -> item.key().equals(post.key())));
        }
    }

    public void removePost(PostFolder folder, Post post) {
        int index = folder.getPosts().indexOf(post);
        if (folder.getPosts().removeIf(item -> item.key().equals(post.key()))) {
            persist(() -> store.removeFromFolder(folder.getName(), post), () -> {
                if (!folder.contains(post.key())) {
                    folder.getPosts().add(Math.min(Math.max(index, 0), folder.getPosts().size()), post);
                }
            });
        }
    }

    private void persist(LibraryStore.SqlTask write, Runnable undo) {
        StoreWrites.persist(store, "folders.saveError", onError, write, undo);
    }
}
