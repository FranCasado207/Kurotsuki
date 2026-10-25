package pt.yaku.ui;

import java.io.IOException;
import java.util.EnumMap;
import java.util.HashSet;
import java.util.List;
import java.util.Objects;
import java.util.Set;
import java.util.Map;

import javafx.beans.property.BooleanProperty;
import javafx.beans.property.SimpleBooleanProperty;
import javafx.collections.ListChangeListener;
import javafx.concurrent.Task;
import javafx.scene.Cursor;
import javafx.scene.Scene;
import javafx.scene.Node;
import javafx.scene.Parent;
import javafx.scene.control.Alert;
import javafx.scene.control.CheckMenuItem;
import javafx.scene.control.Menu;
import javafx.scene.control.MenuItem;
import javafx.scene.control.SeparatorMenuItem;
import javafx.scene.control.Tab;
import javafx.scene.control.TabPane;
import javafx.scene.layout.BorderPane;
import javafx.stage.Stage;
import pt.yaku.booru.Pool;
import pt.yaku.booru.Post;
import pt.yaku.context.ApplicationContext;
import pt.yaku.i18n.Messages;
import pt.yaku.layout.Dock;
import pt.yaku.layout.PanelId;
import pt.yaku.layout.PanelLayout;
import pt.yaku.library.Library;
import pt.yaku.library.PostFolder;
import pt.yaku.storage.LibraryStore.ReadingRecord;
import pt.yaku.user.config.BooruConfiguration;
import pt.yaku.user.config.TagBlacklist;
import pt.yaku.client.RestClientConfiguration;

public class MainView {

    private final Stage stage;
    private final ApplicationContext context;
    private final Library library;
    private final BorderPane root = new BorderPane();
    private final TabPane tabs = new TabPane();
    private final Sidebar sidebar;
    private final Workspace workspace;
    private final BooleanProperty foldersOpen = new SimpleBooleanProperty(false);
    private FoldersTab foldersTab;
    private final BooleanProperty favoritesOpen = new SimpleBooleanProperty(false);
    private FavoritesTab favoritesTab;
    private final BooleanProperty readingOpen = new SimpleBooleanProperty(false);
    private ReadingTab readingTab;

    public MainView(Stage stage, ApplicationContext context) {
        this.stage = stage;
        this.context = context;
        this.library = new Library(context, this::showError);
        this.sidebar = new Sidebar(context, library, this::openSearch, this::openPools);

        tabs.setTabClosingPolicy(TabPane.TabClosingPolicy.ALL_TABS);
        tabs.getSelectionModel().selectedItemProperty().addListener((obs, oldTab, newTab) -> updateTitle());

        PanelLayout panelLayout = new PanelLayout(context, this::showError);
        Map<PanelId, Node> contents = new EnumMap<>(PanelId.class);
        contents.put(PanelId.SEARCH, sidebar.getSearchNode());
        contents.put(PanelId.FAVORITE_TAGS, sidebar.getFavoriteTagsNode());
        contents.put(PanelId.DOWNLOADS, new DownloadBar(library).getNode());
        workspace = new Workspace(panelLayout, contents, tabs);
        workspace.setOnChanged(this::refreshPanelItems);

        foldersOpen.addListener((obs, wasOpen, isOpen) -> {
            if (isOpen) {
                if (foldersTab == null) {
                    foldersTab = new FoldersTab(library, this::openFolder);
                }
                if (!tabs.getTabs().contains(foldersTab)) {
                    addTab(foldersTab);
                }
            } else if (foldersTab != null) {
                tabs.getTabs().remove(foldersTab);
            }
        });

        favoritesOpen.addListener((obs, wasOpen, isOpen) -> {
            if (isOpen) {
                if (favoritesTab == null) {
                    favoritesTab = new FavoritesTab(library, this::openPost);
                }
                if (!tabs.getTabs().contains(favoritesTab)) {
                    addTab(favoritesTab);
                }
            } else if (favoritesTab != null) {
                tabs.getTabs().remove(favoritesTab);
            }
        });

        readingOpen.addListener((obs, wasOpen, isOpen) -> {
            if (isOpen) {
                if (readingTab == null) {
                    readingTab = new ReadingTab(library, this::continueReading);
                }
                if (!tabs.getTabs().contains(readingTab)) {
                    addTab(readingTab);
                }
            } else if (readingTab != null) {
                tabs.getTabs().remove(readingTab);
            }
        });

        tabs.getTabs().addListener((ListChangeListener<Tab>) change -> {
            if (readingTab != null && !tabs.getTabs().contains(readingTab)) {
                readingOpen.set(false);
            }
            if (foldersTab != null && !tabs.getTabs().contains(foldersTab)) {
                foldersOpen.set(false);
            }
            if (favoritesTab != null && !tabs.getTabs().contains(favoritesTab)) {
                favoritesOpen.set(false);
            }
        });

        root.setTop(MainMenuBar.create(stage, context, sidebar::reload, sidebar::focusSearch,
                this::selectAllInCurrentTab, this::bulkDownloadCurrentTab, library.queue()::forgetHistory,
                buildWindowItems()));
        root.setCenter(workspace.getNode());
        favoritesOpen.set(true);
        foldersOpen.set(true);
        // Pools that are still being read are what the user most likely wants to get back to.
        readingOpen.set(library.reading().getEntries().stream().anyMatch(record -> !record.finished()));
    }

    public Parent getRoot() {
        return root;
    }

    private final Map<PanelId, CheckMenuItem> panelItems = new EnumMap<>(PanelId.class);

    private List<MenuItem> buildWindowItems() {
        CheckMenuItem folders = new CheckMenuItem(Messages.get("menu.folders"));
        folders.selectedProperty().bindBidirectional(foldersOpen);
        CheckMenuItem favorites = new CheckMenuItem(Messages.get("favorites.tab"));
        favorites.selectedProperty().bindBidirectional(favoritesOpen);
        CheckMenuItem reading = new CheckMenuItem(Messages.get("reading.tab"));
        reading.selectedProperty().bindBidirectional(readingOpen);

        CheckMenuItem editLayout = new CheckMenuItem(Messages.get("layout.edit"));
        editLayout.selectedProperty().bindBidirectional(workspace.editModeProperty());

        Menu panels = new Menu(Messages.get("layout.panels"));
        for (PanelId id : PanelId.values()) {
            CheckMenuItem item = new CheckMenuItem(Messages.get(id.labelKey()));
            item.setOnAction(event -> {
                boolean hidden = workspace.dockOf(id) == Dock.HIDDEN;
                workspace.move(id, hidden ? id.defaultDock() : Dock.HIDDEN);
            });
            panelItems.put(id, item);
            panels.getItems().add(item);
        }

        MenuItem reset = new MenuItem(Messages.get("layout.reset"));
        reset.setOnAction(event -> workspace.reset());

        refreshPanelItems();
        return List.of(folders, favorites, reading, new SeparatorMenuItem(), editLayout, panels, reset);
    }

    private void refreshPanelItems() {
        if (panelItems.isEmpty()) {
            return;
        }
        for (PanelId id : PanelId.values()) {
            panelItems.get(id).setSelected(workspace.dockOf(id) != Dock.HIDDEN);
        }
    }

    private void openSearch(BooruConfiguration booru, String tags) {
        if (booru == null) {
            showError(Messages.get("main.selectBooru"));
            return;
        }
        if (!RestClientConfiguration.hasClient(booru)) {
            showError(Messages.get("main.noClient"));
            return;
        }
        context.setSelectedBooru(booru);
        addFirstTab(new SearchTab(context, library, booru, tags, this::openPost));
    }

    private void openPools(BooruConfiguration booru) {
        if (booru == null || !booru.supportsPools()) {
            showError(Messages.get("pools.notSupported"));
            return;
        }
        for (Tab tab : tabs.getTabs()) {
            if (tab instanceof PoolsTab poolsTab && poolsTab.getUserData() == booru) {
                tabs.getSelectionModel().select(tab);
                return;
            }
        }
        PoolsTab tab = new PoolsTab(context, library, booru, new PoolsTab.Actions() {
            @Override
            public void read(Pool pool, boolean fromStart) {
                openReader(booru, pool, fromStart);
            }

            @Override
            public void browse(Pool pool) {
                addTab(new PoolTab(context, library, booru, pool, MainView.this::openPost,
                        () -> openReader(booru, pool, false), () -> library.queue().downloadPool(pool)));
            }

            @Override
            public void download(Pool pool) {
                library.queue().downloadPool(pool);
            }

            @Override
            public void openOnWebsite(Pool pool) {
                try {
                    SystemOpener.openUrl(booru.getBaseUrl().replaceAll("/+$", "") + "/pools/" + pool.id());
                } catch (IOException | IllegalArgumentException e) {
                    showError(Messages.get("viewer.browserError", e.getMessage()));
                }
            }
        });
        tab.setUserData(booru);
        addTab(tab);
    }

    /** Fetches the pool's posts in the background, then opens the reader at the saved (or first) page. */
    private void openReader(BooruConfiguration booru, Pool pool, boolean fromStart) {
        Scene scene = stage.getScene();
        if (scene != null) {
            scene.setCursor(Cursor.WAIT);
        }
        Task<List<Post>> task = new Task<>() {
            @Override
            protected List<Post> call() {
                return context.getRestConfig().clientFor(booru).poolPosts(pool);
            }
        };
        task.setOnSucceeded(event -> {
            resetCursor();
            List<Post> received = task.getValue();
            List<Post> posts = received;
            if (context.getUserConfig().isReaderHonorBlacklist()) {
                Set<String> blocked = new HashSet<>(Objects.requireNonNullElse(
                        context.getUserConfig().getBlacklistedTags(), List.of()));
                posts = received.stream().filter(post -> !TagBlacklist.isBlocked(post, blocked)).toList();
            }
            if (posts.isEmpty()) {
                showError(Messages.get("pools.postsEmpty"));
                return;
            }
            int start = fromStart ? 0 : Math.min(library.reading().pageOf(pool.key()), posts.size() - 1);
            if (!fromStart && library.reading().isFinished(pool.key()) && start >= posts.size() - 1) {
                start = 0; // a finished comic is read again from the first page
            }
            new PoolReader(stage, context, library, pool, posts, start, received.size() - posts.size());
        });
        task.setOnFailed(event -> {
            resetCursor();
            showError(Messages.get("pools.error", task.getException().getMessage()));
        });
        Thread thread = new Thread(task, "pool-reader-load");
        thread.setDaemon(true);
        thread.start();
    }

    private void continueReading(ReadingRecord record) {
        for (BooruConfiguration booru : context.getUserConfig().getBooruConfigurations()) {
            if (record.booruBaseUrl().equals(booru.getBaseUrl()) && booru.supportsPools()) {
                openPoolById(booru, record.poolId());
                return;
            }
        }
        showError(Messages.get("reading.noBooru", record.booruBaseUrl()));
    }

    private void openPoolById(BooruConfiguration booru, long id) {
        Task<Pool> task = new Task<>() {
            @Override
            protected Pool call() {
                return context.getRestConfig().clientFor(booru).getPool(id);
            }
        };
        task.setOnSucceeded(event -> {
            if (task.getValue() == null) {
                showError(Messages.get("pools.notFound", String.valueOf(id)));
            } else {
                openReader(booru, task.getValue(), false);
            }
        });
        task.setOnFailed(event -> showError(Messages.get("pools.error", task.getException().getMessage())));
        Thread thread = new Thread(task, "pool-load");
        thread.setDaemon(true);
        thread.start();
    }

    private void resetCursor() {
        if (stage.getScene() != null) {
            stage.getScene().setCursor(Cursor.DEFAULT);
        }
    }

    private void openFolder(PostFolder folder) {
        for (Tab tab : tabs.getTabs()) {
            if (tab instanceof FolderTab folderTab && folderTab.getText().equals(folder.getName())) {
                tabs.getSelectionModel().select(tab);
                return;
            }
        }
        addTab(new FolderTab(library, folder, this::openPost));
    }

    private void selectAllInCurrentTab() {
        Tab selected = tabs.getSelectionModel().getSelectedItem();
        if (selected instanceof GridTab gridTab) {
            gridTab.grid().selectAll();
        }
    }

    private void bulkDownloadCurrentTab() {
        Tab selected = tabs.getSelectionModel().getSelectedItem();
        if (selected instanceof SearchTab search) {
            new BulkDownloadDialog(stage, context, library, search).show();
        } else {
            Alert alert = Styles.alert(Alert.AlertType.INFORMATION, Messages.get("bulk.needSearch"));
            alert.initOwner(stage);
            alert.showAndWait();
        }
    }

    private void addTab(Tab tab) {
        tabs.getTabs().add(tab);
        tabs.getSelectionModel().select(tab);
    }

    private void addFirstTab(Tab tab) {
        tabs.getTabs().add(0, tab);
        tabs.getSelectionModel().select(tab);
    }

    private void updateTitle() {
        Tab selected = tabs.getSelectionModel().getSelectedItem();
        String title = Messages.get("app.title");
        stage.setTitle(selected == null ? title : title + " - " + selected.getText());
    }

    private void openPost(Post post, List<Post> posts) {
        BooruConfiguration booru = booruFor(post);
        new PostViewer(stage, context, library, post, posts,
                booru == null ? null : tag -> openSearch(booru, tag), booru, this::openPoolById);
    }

    private BooruConfiguration booruFor(Post post) {
        if (post.getBooruBaseUrl() == null) {
            return null;
        }
        for (BooruConfiguration booru : context.getUserConfig().getBooruConfigurations()) {
            if (post.getBooruBaseUrl().equals(booru.getBaseUrl())) {
                return booru;
            }
        }
        return null;
    }

    private void showError(String message) {
        Alert alert = Styles.alert(Alert.AlertType.ERROR, message);
        alert.initOwner(stage);
        alert.showAndWait();
    }
}
