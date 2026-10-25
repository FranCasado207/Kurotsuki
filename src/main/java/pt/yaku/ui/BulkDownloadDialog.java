package pt.yaku.ui;

import java.util.HashSet;
import java.util.List;
import java.util.Objects;
import java.util.Set;
import java.util.concurrent.atomic.AtomicBoolean;

import javafx.application.Platform;
import javafx.concurrent.Task;
import javafx.geometry.Insets;
import javafx.scene.control.ButtonBar;
import javafx.scene.control.ButtonType;
import javafx.scene.control.Dialog;
import javafx.scene.control.Label;
import javafx.scene.control.ProgressBar;
import javafx.scene.control.Spinner;
import javafx.scene.layout.HBox;
import javafx.scene.layout.VBox;
import javafx.stage.Window;
import pt.yaku.booru.Post;
import pt.yaku.client.HostThrottle;
import pt.yaku.context.ApplicationContext;
import pt.yaku.download.BulkFetch;
import pt.yaku.i18n.Messages;
import pt.yaku.library.Library;
import pt.yaku.user.config.TagBlacklist;

/** Asks for a page range of the current search and queues every post in it for download. */
public class BulkDownloadDialog extends Dialog<Void> {

    private final ApplicationContext context;
    private final Library library;
    private final SearchTab search;
    private final AtomicBoolean cancelled = new AtomicBoolean();
    private final Spinner<Integer> from = new Spinner<>(1, BulkFetch.MAX_PAGES * 20, 1);
    private final Spinner<Integer> to = new Spinner<>(1, BulkFetch.MAX_PAGES * 20, 5);
    private final ProgressBar progress = new ProgressBar();
    private final Label status = new Label();
    private boolean running;

    public BulkDownloadDialog(Window owner, ApplicationContext context, Library library, SearchTab search) {
        this.context = context;
        this.library = library;
        this.search = search;
        initOwner(owner);
        Styles.apply(getDialogPane());
        setTitle(Messages.get("bulk.title"));
        setHeaderText(Messages.get("bulk.header",
                search.tags() == null ? search.booru().getKindLabel() : search.tags()));

        from.setEditable(true);
        to.setEditable(true);
        from.setPrefWidth(100);
        to.setPrefWidth(100);
        Label hint = new Label(Messages.get("bulk.hint", String.valueOf(SearchTab.pageSize()),
                String.valueOf(BulkFetch.MAX_POSTS), String.valueOf(BulkFetch.MAX_PAGES)));
        hint.setWrapText(true);
        hint.getStyleClass().add("muted-text");
        progress.setMaxWidth(Double.MAX_VALUE);
        progress.setVisible(false);
        status.setWrapText(true);

        VBox content = new VBox(10,
                new HBox(10, new Label(Messages.get("bulk.from")), from, new Label(Messages.get("bulk.to")), to),
                hint, progress, status);
        content.setPadding(new Insets(10));
        content.setPrefWidth(420);
        getDialogPane().setContent(content);

        ButtonType start = new ButtonType(Messages.get("bulk.start"), ButtonBar.ButtonData.OK_DONE);
        getDialogPane().getButtonTypes().addAll(start, ButtonType.CLOSE);
        getDialogPane().lookupButton(start).addEventFilter(javafx.event.ActionEvent.ACTION, event -> {
            event.consume();
            if (!running) {
                begin();
            }
        });
        setOnHidden(event -> cancelled.set(true));
    }

    private void begin() {
        commit(from);
        commit(to);
        int first = from.getValue() - 1;
        int last = Math.max(first, to.getValue() - 1);
        running = true;
        getDialogPane().lookupButton(getDialogPane().getButtonTypes().get(0)).setDisable(true);
        progress.setVisible(true);
        progress.setProgress(ProgressBar.INDETERMINATE_PROGRESS);
        status.setText(Messages.get("bulk.working"));

        Set<String> blocked = new HashSet<>(Objects.requireNonNullElse(
                context.getUserConfig().getBlacklistedTags(), List.of()));
        var client = context.getRestConfig().clientFor(search.booru());
        String host = java.net.URI.create(search.booru().getBaseUrl().strip()).getHost();
        HostThrottle throttle = new HostThrottle(BulkFetch.PAGE_SPACING_MILLIS);
        int pages = Math.min(last - first + 1, BulkFetch.MAX_PAGES);

        Task<BulkFetch.Result> task = new Task<>() {
            @Override
            protected BulkFetch.Result call() {
                return BulkFetch.fetch(page -> client.searchPosts(search.tags(), page, SearchTab.pageSize()),
                        first, last, SearchTab.pageSize(), BulkFetch.MAX_POSTS,
                        post -> !TagBlacklist.isBlocked(post, blocked), throttle, host, cancelled::get,
                        read -> Platform.runLater(() -> {
                            progress.setProgress((double) read / pages);
                            status.setText(Messages.get("bulk.reading", String.valueOf(read), String.valueOf(pages)));
                        }));
            }
        };
        task.setOnSucceeded(event -> finish(task.getValue()));
        task.setOnFailed(event -> {
            running = false;
            status.setText(Messages.get("search.error", String.valueOf(task.getException().getMessage())));
        });
        Thread thread = new Thread(task, "bulk-fetch");
        thread.setDaemon(true);
        thread.start();
    }

    private void finish(BulkFetch.Result result) {
        List<Post> posts = result.posts();
        int added = library.queue().addAll(posts);
        if (added > 0) {
            library.queue().startAll();
        }
        progress.setProgress(1);
        StringBuilder message = new StringBuilder(Messages.get("bulk.done",
                String.valueOf(added), String.valueOf(posts.size() - added)));
        if (result.truncated()) {
            message.append('\n').append(Messages.get("bulk.truncated", String.valueOf(BulkFetch.MAX_POSTS)));
        }
        if (result.error() != null) {
            message.append('\n').append(Messages.get("bulk.partial", result.error()));
        }
        status.setText(message.toString());
        running = false;
    }

    private static void commit(Spinner<Integer> spinner) {
        try {
            spinner.increment(0);
        } catch (RuntimeException ignored) {
            // an unparsable text keeps the previous value
        }
    }
}
