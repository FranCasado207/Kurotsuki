package pt.yaku.ui;

import java.util.List;

import javafx.geometry.Insets;
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
import pt.yaku.library.Library;
import pt.yaku.user.config.BooruConfiguration;
import pt.yaku.user.config.preconfigured.GelbooruConfiguration;
import pt.yaku.i18n.Messages;

public class Sidebar {

    public interface SearchHandler {
        void search(BooruConfiguration booru, String tags);
    }

    private static final int SUGGESTION_LIMIT = 10;

    private final ApplicationContext context;
    private final VBox node = new VBox(10);
    private final ListView<BooruConfiguration> booruList = new ListView<>();
    private final TextField searchField = new TextField();
    private final Button searchButton = new Button(Messages.get("common.search"));
    private final ListView<String> favoriteTagList;
    private volatile BooruConfiguration currentBooru;

    public Sidebar(ApplicationContext context, Library library,
                   SearchHandler handler) {
        this.context = context;
        Favorites favorites = library.favorites();

        node.setPadding(new Insets(10));
        node.setPrefWidth(260);

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


        favoriteTagList = new ListView<>(favorites.getTags());
        favoriteTagList.setPlaceholder(new Label(Messages.get("sidebar.noFavTags")));
        favoriteTagList.setCellFactory(list -> new ListCell<>() {
            @Override
            protected void updateItem(String tag, boolean empty) {
                super.updateItem(tag, empty);
                setText(empty ? null : tag);
            }
        });
        favoriteTagList.setOnMouseClicked(event -> {
            String tag = favoriteTagList.getSelectionModel().getSelectedItem();
            if (event.getButton() == MouseButton.PRIMARY && event.getClickCount() == 2 && tag != null) {
                handler.search(currentBooru, tag);
            }
        });
        MenuItem removeItem = new MenuItem(Messages.get("common.unfavorite"));
        removeItem.setOnAction(event -> {
            String tag = favoriteTagList.getSelectionModel().getSelectedItem();
            if (tag != null) {
                favorites.toggleTag(tag);
            }
        });
        favoriteTagList.setContextMenu(new ContextMenu(removeItem));
        VBox.setVgrow(favoriteTagList, Priority.ALWAYS);

        node.getChildren().addAll(new Label(Messages.get("sidebar.boorusTitle")), booruList, searchField, searchButton,
                new Label(Messages.get("sidebar.favTags")), favoriteTagList);
        reload();
    }

    public VBox getNode() {
        return node;
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
