package pt.yaku.ui;

import java.util.EnumMap;
import java.util.List;
import java.util.Map;

import javafx.beans.property.BooleanProperty;
import javafx.beans.property.SimpleBooleanProperty;
import javafx.collections.ListChangeListener;
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
import pt.yaku.booru.Post;
import pt.yaku.context.ApplicationContext;
import pt.yaku.i18n.Messages;
import pt.yaku.layout.Dock;
import pt.yaku.layout.PanelId;
import pt.yaku.layout.PanelLayout;
import pt.yaku.library.Library;
import pt.yaku.library.PostFolder;
import pt.yaku.user.config.BooruConfiguration;
import pt.yaku.client.RestClientConfiguration;
import pt.yaku.user.config.BooruConfiguration;

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

    public MainView(Stage stage, ApplicationContext context) {
        this.stage = stage;
        this.context = context;
        this.library = new Library(context, this::showError);
        this.sidebar = new Sidebar(context, library, this::openSearch);

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

        tabs.getTabs().addListener((ListChangeListener<Tab>) change -> {
            if (foldersTab != null && !tabs.getTabs().contains(foldersTab)) {
                foldersOpen.set(false);
            }
            if (favoritesTab != null && !tabs.getTabs().contains(favoritesTab)) {
                favoritesOpen.set(false);
            }
        });

        root.setTop(MainMenuBar.create(stage, context, sidebar::reload, buildWindowItems()));
        root.setCenter(workspace.getNode());
        favoritesOpen.set(true);
        foldersOpen.set(true);
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
        return List.of(folders, favorites, new SeparatorMenuItem(), editLayout, panels, reset);
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
        if (!RestClientConfiguration.hasClient(booru.getBooruType())) {
            showError(Messages.get("main.noClient"));
            return;
        }
        context.setSelectedBooru(booru);
        addTab(new SearchTab(context, library, booru, tags, this::openPost));
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

    private void addTab(Tab tab) {
        tabs.getTabs().add(tab);
        tabs.getSelectionModel().select(tab);
    }

    private void updateTitle() {
        Tab selected = tabs.getSelectionModel().getSelectedItem();
        String title = Messages.get("app.title");
        stage.setTitle(selected == null ? title : title + " - " + selected.getText());
    }

    private void openPost(Post post) {
        BooruConfiguration booru = booruFor(post);
        new PostViewer(stage, context, library.favorites(), post,
                booru == null ? null : tag -> openSearch(booru, tag));
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
        Alert alert = new Alert(Alert.AlertType.ERROR, message);
        alert.initOwner(stage);
        alert.showAndWait();
    }
}
