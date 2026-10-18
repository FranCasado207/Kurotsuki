package pt.yaku.ui;

import java.util.Arrays;
import java.util.List;
import java.util.function.Consumer;

import javafx.collections.FXCollections;
import javafx.collections.ListChangeListener;
import javafx.geometry.Insets;
import javafx.scene.control.Button;
import javafx.scene.control.Label;
import javafx.scene.control.ListCell;
import javafx.scene.control.ListView;
import javafx.scene.layout.HBox;
import javafx.scene.layout.Region;
import javafx.scene.layout.Priority;
import javafx.scene.layout.VBox;
import pt.yaku.booru.Post;
import pt.yaku.favorites.Favorites;
import pt.yaku.i18n.Messages;

public class TagPanel extends VBox {

    public TagPanel(Post post, Favorites favorites, Consumer<String> onTagSearch) {
        setSpacing(6);
        setPadding(new Insets(10));
        setPrefWidth(340);
        setMinWidth(340);

        ListView<String> list = new ListView<>(FXCollections.observableArrayList(splitTags(post.getTags())));
        list.setCellFactory(view -> new TagCell(favorites, onTagSearch));
        favorites.getTags().addListener((ListChangeListener<String>) change -> list.refresh());
        VBox.setVgrow(list, Priority.ALWAYS);

        getChildren().addAll(new Label(Messages.get("tagPanel.title")), list);
    }

    static List<String> splitTags(String raw) {
        if (raw == null) {
            return List.of();
        }
        return Arrays.stream(raw.trim().split("\\s+"))
                .filter(tag -> !tag.isBlank())
                .toList();
    }

    private static class TagCell extends ListCell<String> {

        private final Favorites favorites;
        private final HBox row = new HBox(6);
        private final Label name = new Label();
        private final Button favoriteButton = new Button();
        private final Button searchButton = new Button(Messages.get("tagPanel.search"));

        TagCell(Favorites favorites, Consumer<String> onTagSearch) {
            this.favorites = favorites;

            name.setPrefWidth(0);
            name.setMinWidth(0);
            name.setMaxWidth(Double.MAX_VALUE);
            HBox.setHgrow(name, Priority.ALWAYS);
            favoriteButton.setMinWidth(Region.USE_PREF_SIZE);
            searchButton.setMinWidth(Region.USE_PREF_SIZE);
            row.getChildren().addAll(name, favoriteButton, searchButton);

            favoriteButton.setOnAction(event -> favorites.toggleTag(getItem()));
            searchButton.setOnAction(event -> onTagSearch.accept(getItem()));
            searchButton.setDisable(onTagSearch == null);
        }

        @Override
        protected void updateItem(String tag, boolean empty) {
            super.updateItem(tag, empty);
            if (empty || tag == null) {
                setGraphic(null);
                return;
            }
            name.setText(tag);
            favoriteButton.setText(favorites.isTagFavorite(tag) ? Messages.get("tagPanel.unfavorite") : Messages.get("common.favorite"));
            setGraphic(row);
        }
    }
}
