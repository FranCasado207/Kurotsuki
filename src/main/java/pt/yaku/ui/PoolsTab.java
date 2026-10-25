package pt.yaku.ui;

import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.Set;
import java.util.function.Consumer;

import javafx.application.Platform;
import javafx.collections.ListChangeListener;
import javafx.collections.WeakListChangeListener;
import javafx.concurrent.Task;
import javafx.geometry.Insets;
import javafx.geometry.Pos;
import javafx.scene.control.Button;
import javafx.scene.control.ChoiceBox;
import javafx.scene.control.ContextMenu;
import javafx.scene.control.Label;
import javafx.scene.control.ListCell;
import javafx.scene.control.ListView;
import javafx.scene.control.MenuItem;
import javafx.scene.control.Tab;
import javafx.scene.control.TextField;
import javafx.scene.control.Tooltip;
import javafx.scene.image.Image;
import javafx.scene.image.ImageView;
import javafx.scene.input.MouseButton;
import javafx.scene.layout.BorderPane;
import javafx.scene.layout.HBox;
import javafx.scene.layout.Priority;
import javafx.scene.layout.StackPane;
import javafx.scene.layout.VBox;
import javafx.util.StringConverter;
import pt.yaku.booru.BooruClient;
import pt.yaku.booru.Pool;
import pt.yaku.booru.PoolOrder;
import pt.yaku.booru.Post;
import pt.yaku.context.ApplicationContext;
import pt.yaku.i18n.Messages;
import pt.yaku.library.Library;
import pt.yaku.storage.LibraryStore.ReadingRecord;
import pt.yaku.user.config.BooruConfiguration;
import pt.yaku.user.config.TagBlacklist;

/** Browses and searches the pools of one booru, with a cover and the reading status of every pool. */
public class PoolsTab extends Tab {

    public interface Actions {
        void read(Pool pool, boolean fromStart);

        void browse(Pool pool);

        void download(Pool pool);

        void openOnWebsite(Pool pool);
    }

    private static final int PAGE_SIZE = 40;
    private static final int DESCRIPTION_PREVIEW = 500;
    private static final double COVER_WIDTH = 56;
    private static final double COVER_HEIGHT = 76;

    private final ApplicationContext context;
    private final Library library;
    private final BooruConfiguration booru;
    private final Actions actions;
    private final BooruClient client;

    private final TextField searchField = new TextField();
    private final ChoiceBox<String> categoryBox = new ChoiceBox<>();
    private final ChoiceBox<PoolOrder> orderBox = new ChoiceBox<>();
    private final ListView<Pool> list = new ListView<>();
    private final Label status = new Label();
    private final Map<Long, Post> covers = new HashMap<>();
    private final Map<Long, Image> coverImages = new HashMap<>();
    private final ListChangeListener<ReadingRecord> readingListener = change -> list.refresh();

    private int nextPage;
    private int searchId;
    private boolean loading;
    private boolean exhausted;
    private boolean closed;

    public PoolsTab(ApplicationContext context, Library library, BooruConfiguration booru, Actions actions) {
        this.context = context;
        this.library = library;
        this.booru = booru;
        this.actions = actions;
        this.client = context.getRestConfig().clientFor(booru);
        setText(Messages.get("pools.tab") + " - " + booru.getKindLabel());
        setOnClosed(event -> closed = true);

        searchField.setPromptText(Messages.get("pools.searchPrompt"));
        searchField.setOnAction(event -> search());
        HBox.setHgrow(searchField, Priority.ALWAYS);

        categoryBox.getItems().setAll("", Pool.SERIES, Pool.COLLECTION);
        categoryBox.setConverter(new StringConverter<>() {
            @Override
            public String toString(String value) {
                return Messages.get(value == null || value.isEmpty() ? "pools.category.all"
                        : "pools.category." + value);
            }

            @Override
            public String fromString(String text) {
                return null;
            }
        });
        categoryBox.setValue("");
        categoryBox.valueProperty().addListener((obs, was, is) -> search());

        orderBox.getItems().setAll(PoolOrder.values());
        orderBox.setConverter(new StringConverter<>() {
            @Override
            public String toString(PoolOrder order) {
                return order == null ? "" : Messages.get(order.getLabelKey());
            }

            @Override
            public PoolOrder fromString(String text) {
                return null;
            }
        });
        orderBox.setValue(PoolOrder.UPDATED);
        orderBox.valueProperty().addListener((obs, was, is) -> search());

        Button searchButton = new Button(Messages.get("common.search"));
        searchButton.getStyleClass().add("accent");
        searchButton.setOnAction(event -> search());

        HBox controls = new HBox(8, searchField);
        if (client.supportsPoolFilters()) {
            controls.getChildren().addAll(categoryBox, orderBox);
        }
        controls.getChildren().add(searchButton);
        controls.setPadding(new Insets(10));

        list.setPlaceholder(new Label(Messages.get("pools.loading")));
        list.setCellFactory(view -> new PoolCell());
        list.setOnMouseClicked(event -> {
            Pool pool = list.getSelectionModel().getSelectedItem();
            if (event.getButton() == MouseButton.PRIMARY && event.getClickCount() == 2 && pool != null) {
                actions.read(pool, false);
            }
        });
        list.setContextMenu(contextMenu());
        library.reading().getEntries().addListener(new WeakListChangeListener<>(readingListener));
        status.getStyleClass().add("pool-status");
        status.setPadding(new Insets(6, 10, 6, 10));

        BorderPane content = new BorderPane();
        content.setTop(controls);
        content.setCenter(list);
        content.setBottom(status);
        setContent(content);

        search();
    }

    private ContextMenu contextMenu() {
        MenuItem read = new MenuItem(Messages.get("pools.read"));
        read.setOnAction(event -> selected(pool -> actions.read(pool, false)));
        MenuItem readFromStart = new MenuItem(Messages.get("pools.readFromStart"));
        readFromStart.setOnAction(event -> selected(pool -> actions.read(pool, true)));
        MenuItem browse = new MenuItem(Messages.get("pools.browse"));
        browse.setOnAction(event -> selected(actions::browse));
        MenuItem download = new MenuItem(Messages.get("pools.download"));
        download.setOnAction(event -> selected(actions::download));
        MenuItem toggleFinished = new MenuItem();
        toggleFinished.setOnAction(event -> selected(pool ->
                library.reading().setFinished(pool, !library.reading().isFinished(pool.key()))));
        MenuItem forget = new MenuItem(Messages.get("reading.forget"));
        forget.setOnAction(event -> selected(pool -> library.reading().remove(pool.key())));
        MenuItem website = new MenuItem(Messages.get("pools.openSite"));
        website.setOnAction(event -> selected(actions::openOnWebsite));
        ContextMenu menu = new ContextMenu(read, readFromStart, browse, download,
                new javafx.scene.control.SeparatorMenuItem(), toggleFinished, forget,
                new javafx.scene.control.SeparatorMenuItem(), website);
        menu.setOnShowing(event -> {
            Pool pool = list.getSelectionModel().getSelectedItem();
            menu.getItems().forEach(item -> item.setDisable(pool == null));
            boolean finished = pool != null && library.reading().isFinished(pool.key());
            toggleFinished.setText(Messages.get(finished ? "reading.markReading" : "reading.markFinished"));
            forget.setDisable(pool == null || library.reading().get(pool.key()) == null);
        });
        return menu;
    }

    private void selected(Consumer<Pool> action) {
        Pool pool = list.getSelectionModel().getSelectedItem();
        if (pool != null) {
            action.accept(pool);
        }
    }

    private void search() {
        searchId++;
        nextPage = 0;
        exhausted = false;
        loading = false;
        list.getItems().clear();
        covers.clear();
        coverImages.clear();
        list.setPlaceholder(new Label(Messages.get("pools.loading")));
        loadNextPage();
    }

    private void loadNextPage() {
        if (loading || exhausted || closed) {
            return;
        }
        loading = true;
        int id = searchId;
        int page = nextPage;
        String query = searchField.getText();
        String category = categoryBox.getValue() == null || categoryBox.getValue().isEmpty()
                ? null : categoryBox.getValue();
        PoolOrder order = orderBox.getValue();
        status.setText(Messages.get(page == 0 ? "pools.loading" : "search.loadingMore"));

        Task<List<Pool>> task = new Task<>() {
            @Override
            protected List<Pool> call() {
                return client.searchPools(query, category, order, page, PAGE_SIZE);
            }
        };
        task.setOnSucceeded(event -> {
            if (id != searchId || closed) {
                return;
            }
            loading = false;
            List<Pool> received = task.getValue();
            nextPage = page + 1;
            exhausted = received.size() < client.poolPageSize(PAGE_SIZE);
            list.getItems().addAll(received);
            status.setText(exhausted ? Messages.get("pools.count", String.valueOf(list.getItems().size())) : "");
            if (list.getItems().isEmpty()) {
                list.setPlaceholder(new Label(Messages.get("pools.empty")));
            }
            loadCovers(id, received);
        });
        task.setOnFailed(event -> {
            if (id != searchId || closed) {
                return;
            }
            loading = false;
            String message = Messages.get("pools.error", task.getException().getMessage());
            status.setText(message);
            list.setPlaceholder(new Label(message));
        });
        Thread thread = new Thread(task, "pool-search");
        thread.setDaemon(true);
        thread.start();
    }

    /** Fetches the first post of each new pool in a few batched requests, then redraws the list. */
    private void loadCovers(int id, List<Pool> pools) {
        if (pools.isEmpty()) {
            return;
        }
        Set<String> blocked = new HashSet<>(Objects.requireNonNullElse(
                context.getUserConfig().getBlacklistedTags(), List.of()));
        Task<Map<Long, Post>> task = new Task<>() {
            @Override
            protected Map<Long, Post> call() {
                return client.poolCovers(pools);
            }
        };
        task.setOnSucceeded(event -> {
            if (id != searchId || closed) {
                return;
            }
            task.getValue().forEach((poolId, post) -> {
                if (!TagBlacklist.isBlocked(post, blocked)) {
                    covers.put(poolId, post);
                }
            });
            list.refresh();
        });
        // A missing cover is not worth an error message: the row simply keeps its placeholder.
        Thread thread = new Thread(task, "pool-covers");
        thread.setDaemon(true);
        thread.start();
    }

    private String statusOf(Pool pool) {
        ReadingRecord record = library.reading().get(pool.key());
        if (record == null) {
            return null;
        }
        return record.finished() ? Messages.get("reading.finishedLabel")
                : Messages.get("reading.inProgress", String.valueOf(record.page() + 1),
                        String.valueOf(Math.max(record.total(), pool.postCount())));
    }

    private final class PoolCell extends ListCell<Pool> {

        private final ImageView cover = new ImageView();
        private final StackPane coverBox = new StackPane(cover);
        private final Label name = new Label();
        private final Label meta = new Label();
        private final Label state = new Label();
        private final VBox text = new VBox(2, name, meta, state);
        private final HBox row = new HBox(10, coverBox, text);
        private long shownPool = -1;

        PoolCell() {
            name.getStyleClass().add("pool-name");
            meta.getStyleClass().add("pool-meta");
            state.getStyleClass().add("pool-state");
            cover.setFitWidth(COVER_WIDTH);
            cover.setFitHeight(COVER_HEIGHT);
            cover.setPreserveRatio(true);
            coverBox.getStyleClass().add("pool-cover");
            coverBox.setMinSize(COVER_WIDTH, COVER_HEIGHT);
            coverBox.setPrefSize(COVER_WIDTH, COVER_HEIGHT);
            coverBox.setMaxSize(COVER_WIDTH, COVER_HEIGHT);
            row.setAlignment(Pos.CENTER_LEFT);
            row.setPadding(new Insets(4, 2, 4, 2));
        }

        @Override
        protected void updateItem(Pool pool, boolean empty) {
            super.updateItem(pool, empty);
            if (empty || pool == null) {
                shownPool = -1;
                setGraphic(null);
                setTooltip(null);
                return;
            }
            name.setText(pool.displayName());
            String category = pool.category() == null ? null
                    : Messages.get(pool.isSeries() ? "pools.category.series" : "pools.category.collection");
            String updated = pool.updatedAt() == null || pool.updatedAt().length() < 10 ? null
                    : Messages.get("pools.updated", pool.updatedAt().substring(0, 10));
            meta.setText(String.join("  ·  ", java.util.stream.Stream.of(category,
                    Messages.get("pools.posts", String.valueOf(pool.postCount())), updated)
                    .filter(Objects::nonNull).toList()));
            String readingState = statusOf(pool);
            state.setText(readingState == null ? "" : readingState);
            state.setVisible(readingState != null);
            state.setManaged(readingState != null);
            showCover(pool);
            setGraphic(row);
            String description = pool.description();
            if (description == null || description.isBlank()) {
                setTooltip(null);
            } else {
                setTooltip(new Tooltip(description.length() > DESCRIPTION_PREVIEW
                        ? description.substring(0, DESCRIPTION_PREVIEW) + "…" : description));
            }
            // Load the next page when the user gets close to the end of the list.
            if (getIndex() >= getListView().getItems().size() - 5) {
                Platform.runLater(PoolsTab.this::loadNextPage);
            }
        }

        private void showCover(Pool pool) {
            Image cached = coverImages.get(pool.id());
            if (cached != null) {
                cover.setImage(cached);
                shownPool = pool.id();
                return;
            }
            if (shownPool != pool.id()) {
                cover.setImage(null);
            }
            shownPool = pool.id();
            Post post = covers.get(pool.id());
            if (post == null || post.getPreviewUrl() == null) {
                return;
            }
            long wanted = pool.id();
            library.images().load(post.getPreviewUrl(), COVER_HEIGHT).thenAccept(image -> {
                if (image != null) {
                    Platform.runLater(() -> {
                        coverImages.put(wanted, image);
                        if (shownPool == wanted) {
                            cover.setImage(image);
                        }
                    });
                }
            });
        }
    }
}
