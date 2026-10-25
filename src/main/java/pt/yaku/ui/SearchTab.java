package pt.yaku.ui;

import java.util.HashSet;
import java.util.List;
import java.util.Objects;
import java.util.Set;
import java.util.function.Consumer;
import java.util.function.BiConsumer;

import javafx.concurrent.Task;
import javafx.scene.control.Tab;
import pt.yaku.booru.Post;
import pt.yaku.context.ApplicationContext;
import pt.yaku.library.Library;
import pt.yaku.user.config.TagBlacklist;
import pt.yaku.user.config.BooruConfiguration;
import pt.yaku.i18n.Messages;

public class SearchTab extends Tab implements GridTab {

    private static final int PAGE_SIZE = 100;
    private static final java.util.logging.Logger LOGGER = java.util.logging.Logger.getLogger(SearchTab.class.getName());

    private static String hostOf(BooruConfiguration booru) {
        try {
            return java.net.URI.create(booru.getBaseUrl().strip()).getHost();
        } catch (RuntimeException e) {
            return "?";
        }
    }

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
                     String tags, BiConsumer<Post, List<Post>> onPostClicked) {
        this.context = context;
        this.booru = booru;
        this.tags = tags == null || tags.isBlank() ? null : tags.trim();

        imageGrid = new ImageGrid(library);
        imageGrid.setOnPostClicked(onPostClicked);
        imageGrid.setOnNearBottom(this::loadNextPage);

        setText(this.tags != null ? this.tags : booru.getKindLabel());
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
                long started = System.nanoTime();
                try {
                    List<Post> result = context.getRestConfig().clientFor(booru).searchPosts(tags, page, PAGE_SIZE);
                    LOGGER.info("Search ok: booru=" + booru.getEngine() + " host=" + hostOf(booru) + " page=" + page
                            + " tagCount=" + (tags == null ? 0 : tags.split("\\s+").length)
                            + " results=" + result.size() + " ms=" + (System.nanoTime() - started) / 1_000_000);
                    return result;
                } catch (RuntimeException e) {
                    LOGGER.warning("Search failed: booru=" + booru.getEngine() + " host=" + hostOf(booru) + " page="
                            + page + " ms=" + (System.nanoTime() - started) / 1_000_000 + " error=" + e);
                    throw e;
                }
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
            imageGrid.setStatus(hidden > 0 ? Messages.get("search.hidden", String.valueOf(hidden)) : "");
            if (page == 0 && visible.isEmpty()) {
                imageGrid.showEmptyState(Messages.get("search.noResults"));
            } else if (exhausted) {
                imageGrid.showEndState(Messages.get("search.end"));
            }
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

    public BooruConfiguration booru() {
        return booru;
    }

    public String tags() {
        return tags;
    }

    public static int pageSize() {
        return PAGE_SIZE;
    }

    @Override
    public ImageGrid grid() {
        return imageGrid;
    }
}
