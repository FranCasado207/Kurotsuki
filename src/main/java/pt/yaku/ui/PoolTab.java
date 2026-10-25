package pt.yaku.ui;

import java.util.HashSet;
import java.util.List;
import java.util.Objects;
import java.util.Set;
import java.util.function.BiConsumer;

import javafx.concurrent.Task;
import javafx.geometry.Insets;
import javafx.geometry.Pos;
import javafx.scene.control.Button;
import javafx.scene.control.Label;
import javafx.scene.control.Tab;
import javafx.scene.layout.BorderPane;
import javafx.scene.layout.HBox;
import javafx.scene.layout.Priority;
import javafx.scene.layout.Region;
import pt.yaku.booru.Pool;
import pt.yaku.booru.Post;
import pt.yaku.context.ApplicationContext;
import pt.yaku.i18n.Messages;
import pt.yaku.library.Library;
import pt.yaku.user.config.BooruConfiguration;
import pt.yaku.user.config.TagBlacklist;

/** The posts of one pool as a thumbnail grid, in reading order. */
public class PoolTab extends Tab implements GridTab {

    private final ImageGrid grid;

    public PoolTab(ApplicationContext context, Library library, BooruConfiguration booru, Pool pool,
                   BiConsumer<Post, List<Post>> onPostClicked, Runnable onRead, Runnable onDownload) {
        setText(pool.displayName());
        grid = new ImageGrid(library);
        grid.setOnPostClicked(onPostClicked);

        Label title = new Label(pool.displayName() + "  ·  "
                + Messages.get("pools.posts", String.valueOf(pool.postCount())));
        title.getStyleClass().add("pool-name");
        Region spacer = new Region();
        HBox.setHgrow(spacer, Priority.ALWAYS);
        Button read = new Button(Messages.get("pools.read"));
        read.getStyleClass().add("accent");
        read.setOnAction(event -> onRead.run());
        Button download = new Button(Messages.get("pools.download"));
        download.setOnAction(event -> onDownload.run());
        HBox header = new HBox(10, title, spacer, download, read);
        header.setAlignment(Pos.CENTER_LEFT);
        header.setPadding(new Insets(8, 10, 8, 10));

        BorderPane content = new BorderPane();
        content.setTop(header);
        content.setCenter(grid.getNode());
        setContent(content);

        grid.showLoading();
        Set<String> blocked = new HashSet<>(Objects.requireNonNullElse(
                context.getUserConfig().getBlacklistedTags(), List.of()));
        Task<List<Post>> task = new Task<>() {
            @Override
            protected List<Post> call() {
                return context.getRestConfig().clientFor(booru).poolPosts(pool);
            }
        };
        task.setOnSucceeded(event -> {
            List<Post> received = task.getValue();
            List<Post> visible = received.stream().filter(post -> !TagBlacklist.isBlocked(post, blocked)).toList();
            int hidden = received.size() - visible.size();
            grid.showPosts(visible);
            grid.setStatus(hidden > 0 ? Messages.get("search.hidden", String.valueOf(hidden)) : "");
            if (visible.isEmpty()) {
                grid.showEmptyState(Messages.get("pools.postsEmpty"));
            }
        });
        task.setOnFailed(event -> grid.showMessage(
                Messages.get("search.error", task.getException().getMessage())));
        Thread thread = new Thread(task, "pool-posts");
        thread.setDaemon(true);
        thread.start();
    }

    @Override
    public ImageGrid grid() {
        return grid;
    }
}
