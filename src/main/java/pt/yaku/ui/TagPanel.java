package pt.yaku.ui;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.EnumMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.function.Consumer;

import javafx.collections.ListChangeListener;
import javafx.geometry.Insets;
import javafx.scene.control.ContextMenu;
import javafx.scene.control.Label;
import javafx.scene.control.MenuItem;
import javafx.scene.control.ScrollPane;
import javafx.scene.layout.Priority;
import javafx.scene.layout.Region;
import javafx.scene.layout.VBox;
import pt.yaku.booru.Post;
import pt.yaku.booru.TagCategory;
import pt.yaku.favorites.Favorites;
import pt.yaku.favorites.TagGroups;
import pt.yaku.i18n.Messages;

public class TagPanel extends VBox {

    private static final String CHIP_CLASS = "tag-chip";
    private static final String FAVORITE_CLASS = "tag-chip-favorite";

    private final VBox chips = new VBox(4);
    private final Favorites favorites;
    private final TagGroups tagGroups;
    private Post post;
    private final Consumer<String> onTagSearch;

    public TagPanel(Post post, Favorites favorites, TagGroups tagGroups, Consumer<String> onTagSearch) {
        this.favorites = favorites;
        this.tagGroups = tagGroups;
        getStyleClass().add("card");
        setSpacing(8);
        setPadding(new Insets(12));
        setPrefWidth(260);
        setMinWidth(260);

        this.post = post;
        this.onTagSearch = onTagSearch;
        rebuild();
        favorites.getTags().addListener((ListChangeListener<String>) change -> refreshFavoriteMarks());

        ScrollPane scroll = new ScrollPane(chips);
        scroll.setFitToWidth(true);
        scroll.setHbarPolicy(ScrollPane.ScrollBarPolicy.NEVER);
        VBox.setVgrow(scroll, Priority.ALWAYS);

        Label title = new Label(Messages.get("tagPanel.title"));
        title.getStyleClass().add("section-title");
        getChildren().addAll(title, scroll);
    }

    public void setPost(Post post) {
        this.post = post;
        rebuild();
    }

    public void rebuild() {
        chips.getChildren().clear();
        Map<TagCategory, List<String>> groups = new EnumMap<>(TagCategory.class);
        for (String tag : splitTags(post.getTags())) {
            groups.computeIfAbsent(post.categoryOf(tag), category -> new ArrayList<>()).add(tag);
        }
        for (Map.Entry<TagCategory, List<String>> group : groups.entrySet()) {
            List<String> tags = group.getValue();
            tags.sort(String.CASE_INSENSITIVE_ORDER);
            String key = group.getKey().name().toLowerCase(Locale.ROOT);

            Label header = new Label(Messages.get("tag.category." + key));
            header.getStyleClass().addAll("tag-group", "tag-group-" + key);
            chips.getChildren().add(header);
            for (String tag : tags) {
                chips.getChildren().add(chip(tag, key, onTagSearch));
            }
        }
    }

    static List<String> splitTags(String raw) {
        if (raw == null) {
            return List.of();
        }
        return Arrays.stream(raw.trim().split("\\s+"))
                .filter(tag -> !tag.isBlank())
                .toList();
    }

    private Label chip(String tag, String categoryKey, Consumer<String> onTagSearch) {
        Label chip = new Label(tag);
        chip.getStyleClass().addAll(CHIP_CLASS, "tag-chip-" + categoryKey);
        chip.setUserData(tag);
        chip.setWrapText(false);
        chip.setMaxWidth(Region.USE_PREF_SIZE);
        chip.setContextMenu(chipMenu(tag, onTagSearch));
        chip.setOnDragDetected(event -> {
            javafx.scene.input.Dragboard board = chip.startDragAndDrop(javafx.scene.input.TransferMode.COPY);
            javafx.scene.input.ClipboardContent content = new javafx.scene.input.ClipboardContent();
            content.putString(tag);
            board.setContent(content);
            event.consume();
        });
        applyFavoriteMark(chip);
        return chip;
    }

    private ContextMenu chipMenu(String tag, Consumer<String> onTagSearch) {
        MenuItem favorite = new MenuItem();
        favorite.setOnAction(event -> favorites.toggleTag(tag));
        MenuItem search = new MenuItem(Messages.get("tagPanel.search"));
        search.setOnAction(event -> onTagSearch.accept(tag));

        ContextMenu menu = new ContextMenu(favorite, search, TagGroupMenu.create(tagGroups, tag, this::window));
        menu.setOnShowing(event -> {
            favorite.setText(favorites.isTagFavorite(tag)
                    ? Messages.get("tagPanel.unfavorite") : Messages.get("common.favorite"));
            search.setDisable(onTagSearch == null);
        });
        return menu;
    }

    private javafx.stage.Window window() {
        return getScene() == null ? null : getScene().getWindow();
    }

    private void refreshFavoriteMarks() {
        for (var node : chips.getChildren()) {
            if (node instanceof Label label && label.getUserData() instanceof String) {
                applyFavoriteMark(label);
            }
        }
    }

    private void applyFavoriteMark(Label chip) {
        boolean favorite = favorites.isTagFavorite((String) chip.getUserData());
        chip.getStyleClass().removeIf(FAVORITE_CLASS::equals);
        if (favorite) {
            chip.getStyleClass().add(FAVORITE_CLASS);
        }
    }
}
