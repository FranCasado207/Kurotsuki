package pt.yaku.library;

import java.io.IOException;
import java.util.ArrayList;
import java.util.List;
import java.util.Objects;
import java.util.function.Consumer;

import javafx.collections.FXCollections;
import javafx.collections.ObservableList;
import pt.yaku.client.GelbooruPost;
import pt.yaku.context.ApplicationContext;
import pt.yaku.user.config.FolderData;
import pt.yaku.i18n.Messages;

public class PostFolders {

    private final ApplicationContext context;
    private final Consumer<String> onError;
    private final ObservableList<PostFolder> folders = FXCollections.observableArrayList();

    public PostFolders(ApplicationContext context, Consumer<String> onError) {
        this.context = context;
        this.onError = onError;
        for (FolderData data : Objects.requireNonNullElse(context.getUserConfig().getPostFolders(), List.<FolderData>of())) {
            folders.add(new PostFolder(data.getName(),
                    Objects.requireNonNullElse(data.getPosts(), List.<GelbooruPost>of())));
        }
    }

    public ObservableList<PostFolder> getFolders() {
        return folders;
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
        persist();
        return folder;
    }

    public void delete(PostFolder folder) {
        folders.remove(folder);
        persist();
    }

    public void addPost(PostFolder folder, GelbooruPost post) {
        if (!folder.contains(post.getId())) {
            folder.getPosts().add(post);
            persist();
        }
    }

    public void removePost(PostFolder folder, GelbooruPost post) {
        if (folder.getPosts().removeIf(item -> item.getId() == post.getId())) {
            persist();
        }
    }

    private void persist() {
        List<FolderData> data = new ArrayList<>();
        for (PostFolder folder : folders) {
            data.add(new FolderData(folder.getName(), new ArrayList<>(folder.getPosts())));
        }
        context.getUserConfig().setPostFolders(data);
        try {
            context.saveUserConfig();
        } catch (IOException e) {
            onError.accept(Messages.get("folders.saveError", e.getMessage()));
        }
    }
}
