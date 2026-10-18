package pt.yaku.ui;

import java.util.List;

import javafx.scene.control.Button;
import javafx.scene.control.ContextMenu;
import javafx.scene.control.Label;
import javafx.scene.control.ListCell;
import javafx.scene.control.ListView;
import javafx.scene.control.MenuItem;
import javafx.scene.control.TextField;
import javafx.scene.input.MouseButton;
import javafx.scene.layout.Priority;
import javafx.scene.layout.VBox;
import pt.yaku.context.ApplicationContext;
import pt.yaku.favorites.Favorites;
import pt.yaku.i18n.Messages;
import pt.yaku.library.Library;
import pt.yaku.user.config.BooruConfiguration;
import pt.yaku.user.config.preconfigured.GelbooruConfiguration;

public class Sidebar {

    public interface SearchHandler {
        void search(BooruConfiguration booru, String tags);
    }

    private static final int SUGGESTION_LIMIT = 10;

    private final ApplicationContext context;
    private final VBox searchNode = new VBox(10);
    private final VBox favoriteTagsNode = new VBox(10);
    private final ListView<BooruConfiguration> booruList = new ListView<>();
    private final TextField searchField = new TextField();
    private final Button searchButton = new Button(Messages.get("common.search"));
    private volatile BooruConfiguration currentBooru;

    public Sidebar(ApplicationContext context, Library library, SearchHandler handler) {
        this.context = context;
        Favorites favorites = library.favorites();


        booruList.setCellFactory(list -> new ListCell<>() {
            @Override
            protected void updateItem(BooruConfiguration item, boolean empty) {
                super.updateItem(item, empty);
                setText(empty || item == null ? null : describe(item));
            }
        });
        booruList.setPlaceholder(new Label(Messages.get("sidebar.noBoorus")));
        VBox.setVgrow(booruList, Priority.ALWAYS);

        booruList.getSelectionModel().selectedItemProperty().addListener((obs, oldBooru, newBooru) -> {
            currentBooru = newBooru;
            if (newBooru != null) {
                context.setSelectedBooru(newBooru);
            }
        });

        searchField.setPromptText(Messages.get("sidebar.searchPrompt"));
        searchField.setOnAction(event -> searchButton.fire());
        new TagAutocomplete(searchField, this::suggestTags);

        searchButton.setMaxWidth(Double.MAX_VALUE);
        searchButton.disableProperty().bind(booruList.getSelectionModel().selectedItemProperty().isNull());
        searchButton.setOnAction(event ->
                handler.search(booruList.getSelectionModel().getSelectedItem(), searchField.getText()));

        Label boorusTitle = new Label(Messages.get("sidebar.boorusTitle"));
        searchNode.getChildren().addAll(boorusTitle,
                booruList, searchField, searchButton);

        buildFavoriteTags(favorites, handler);

        reload();
    }

    private void buildFavoriteTags(Favorites favorites, SearchHandler handler) {

        ListView<String> tagList = new ListView<>(favorites.getTags());
        tagList.setPlaceholder(new Label(Messages.get("sidebar.noFavTags")));
        tagList.setCellFactory(list -> new ListCell<>() {
            @Override
            protected void updateItem(String tag, boolean empty) {
                super.updateItem(tag, empty);
                setText(empty ? null : tag);
            }
        });
        tagList.setOnMouseClicked(event -> {
            String tag = tagList.getSelectionModel().getSelectedItem();
            if (event.getButton() == MouseButton.PRIMARY && event.getClickCount() == 2 && tag != null) {
                handler.search(currentBooru, tag);
            }
        });
        MenuItem removeItem = new MenuItem(Messages.get("common.unfavorite"));
        removeItem.setOnAction(event -> {
            String tag = tagList.getSelectionModel().getSelectedItem();
            if (tag != null) {
                favorites.toggleTag(tag);
            }
        });
        tagList.setContextMenu(new ContextMenu(removeItem));
        VBox.setVgrow(tagList, Priority.ALWAYS);

        Label favTagsTitle = new Label(Messages.get("sidebar.favTags"));
        favoriteTagsNode.getChildren().addAll(favTagsTitle, tagList);
    }

    public VBox getSearchNode() {
        return searchNode;
    }

    public VBox getFavoriteTagsNode() {
        return favoriteTagsNode;
    }

    public void reload() {
        booruList.getItems().setAll(context.getUserConfig().getBooruConfigurations());
    }

    private List<String> suggestTags(String prefix) throws Exception {
        BooruConfiguration booru = currentBooru;
        if (!(booru instanceof GelbooruConfiguration gelbooru)) {
            return List.of();
        }
        return context.getRestConfig().gelbooru(gelbooru).listTags(
                "dapi", "tag", "index",
                gelbooru.getApiKey(), gelbooru.getUserId(),
                prefix + "%", SUGGESTION_LIMIT, "count", "DESC", 1)
                .stream()
                .map(tag -> tag.getName())
                .toList();
    }

    private static String describe(BooruConfiguration booru) {
        return booru.getBooruType().name() + " - " + booru.getBaseUrl();
    }
}
