package pt.yaku.ui;

import java.util.HashSet;
import java.util.List;
import java.util.Objects;
import java.util.Set;
import java.util.function.Consumer;

import javafx.application.Platform;
import javafx.concurrent.Task;
import javafx.scene.control.Tab;
import pt.yaku.booru.Post;
import pt.yaku.context.ApplicationContext;
import pt.yaku.library.Library;
import pt.yaku.user.config.TagBlacklist;
import pt.yaku.user.config.BooruConfiguration;
import pt.yaku.i18n.Messages;

public class SearchTab extends Tab {

    private static final int PAGE_SIZE = 100;

    private final ApplicationContext context;
    private final BooruConfiguration booru;
    private final String tags;
    private final ImageGrid imageGrid;

    private int nextPage;
    private int searchId;
    private boolean loading;
    private boolean exhausted;
    private boolean closed;

    public SearchTab(ApplicationContext context, Library library, BooruConfiguration booru,
                     String tags, Consumer<Post> onPostClicked) {
        this.context = context;
        this.booru = booru;
        this.tags = tags == null || tags.isBlank() ? null : tags.trim();

        imageGrid = new ImageGrid(library);
        imageGrid.setOnPostClicked(onPostClicked);
        imageGrid.setOnNearBottom(this::loadNextPage);

        setText(this.tags != null ? this.tags : booru.getBooruType().name());
        setContent(imageGrid.getNode());
        setOnClosed(event -> closed = true);

        imageGrid.showLoading();
        loadNextPage();
    }

    private void loadNextPage() {
        if (loading || exhausted || closed) {
            return;
        }
        loading = true;
        int id = searchId;
        int page = nextPage;

        if (page > 0) {
            imageGrid.setStatus(Messages.get("search.loadingMore"));
        }

        Set<String> blocked = new HashSet<>(Objects.requireNonNullElse(
                context.getUserConfig().getBlacklistedTags(), List.of()));

        Task<List<Post>> task = new Task<>() {
            @Override
            protected List<Post> call() {
                return context.getRestConfig().clientFor(booru).searchPosts(tags, page, PAGE_SIZE);
            }
        };
        task.setOnSucceeded(event -> {
            if (id != searchId || closed) {
                return;
            }
            loading = false;
            List<Post> received = task.getValue();
            nextPage = page + 1;
            exhausted = received.size() < PAGE_SIZE;

            List<Post> visible = received.stream()
                    .filter(post -> !TagBlacklist.isBlocked(post, blocked))
                    .toList();
            int hidden = received.size() - visible.size();
            if (page == 0) {
                imageGrid.showPosts(visible);
            } else {
                imageGrid.appendPosts(visible);
            }
            String summary = exhausted ? Messages.get("search.end") : Messages.get("search.showing");
            imageGrid.setStatus(hidden > 0 ? Messages.get("search.hidden", summary, String.valueOf(hidden)) : summary);
        });
        task.setOnFailed(event -> {
            if (id != searchId || closed) {
                return;
            }
            loading = false;
            String message = Messages.get("search.error", task.getException().getMessage());
            if (page == 0) {
                imageGrid.showMessage(message);
            } else {
                imageGrid.setStatus(message);
            }
        });

        Thread thread = new Thread(task, "booru-search");
        thread.setDaemon(true);
        thread.start();
    }
}
