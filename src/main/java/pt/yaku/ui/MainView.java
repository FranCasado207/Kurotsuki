package pt.yaku.ui;

import javafx.beans.property.BooleanProperty;
import javafx.beans.property.SimpleBooleanProperty;
import javafx.collections.ListChangeListener;
import javafx.scene.Parent;
import javafx.scene.control.Alert;
import javafx.scene.control.Tab;
import javafx.scene.control.TabPane;
import javafx.scene.layout.BorderPane;
import javafx.stage.Stage;
import pt.yaku.client.GelbooruPost;
import pt.yaku.context.ApplicationContext;
import pt.yaku.library.Library;
import pt.yaku.library.PostFolder;
import pt.yaku.user.config.BooruConfiguration;
import pt.yaku.user.config.preconfigured.GelbooruConfiguration;
import pt.yaku.i18n.Messages;

public class MainView {

    private final Stage stage;
    private final ApplicationContext context;
    private final Library library;
    private final BorderPane root = new BorderPane();
    private final TabPane tabs = new TabPane();
    private final Sidebar sidebar;
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

        root.setTop(MainMenuBar.create(stage, context, sidebar::reload, foldersOpen, favoritesOpen));
        root.setLeft(sidebar.getNode());
        root.setCenter(tabs);
        root.setBottom(new DownloadBar(library).getNode());
        favoritesOpen.set(true);
        foldersOpen.set(true);
    }

    public Parent getRoot() {
        return root;
    }

    private void openSearch(BooruConfiguration booru, String tags) {
        if (booru == null) {
            showError(Messages.get("main.selectBooru"));
            return;
        }
        if (!(booru instanceof GelbooruConfiguration gelbooru)) {
            showError(Messages.get("main.noClient"));
            return;
        }
        context.setSelectedBooru(gelbooru);
        addTab(new SearchTab(context, library, gelbooru, tags, this::openPost));
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

    private void updateTitle() {
        Tab selected = tabs.getSelectionModel().getSelectedItem();
        String title = Messages.get("app.title");
        stage.setTitle(selected == null ? title : title + " - " + selected.getText());
    }

    private void addTab(Tab tab) {
        tabs.getTabs().add(tab);
        tabs.getSelectionModel().select(tab);
    }

    private void openPost(GelbooruPost post) {
        BooruConfiguration booru = booruFor(post);
        new PostViewer(stage, context, library.favorites(), post,
                booru == null ? null : tag -> openSearch(booru, tag));
    }

    private BooruConfiguration booruFor(GelbooruPost post) {
        if (post.getBooruBaseUrl() == null) {
            return null;
        }
        for (BooruConfiguration booru : context.getUserConfig().getBooruConfigurations()) {
            if (booru instanceof GelbooruConfiguration && post.getBooruBaseUrl().equals(booru.getBaseUrl())) {
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
