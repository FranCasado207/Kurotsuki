package pt.yaku.ui;

import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

import javafx.application.Platform;
import javafx.css.PseudoClass;
import javafx.collections.ListChangeListener;
import javafx.scene.control.Alert;
import javafx.scene.control.Button;
import javafx.scene.control.ContextMenu;
import javafx.scene.control.Label;
import javafx.scene.control.ListCell;
import javafx.scene.control.ListView;
import javafx.scene.control.MenuItem;
import javafx.scene.control.SeparatorMenuItem;
import javafx.scene.control.TextField;
import javafx.scene.control.TreeCell;
import javafx.scene.control.TreeItem;
import javafx.scene.control.TreeView;
import javafx.scene.input.ClipboardContent;
import javafx.scene.input.Dragboard;
import javafx.scene.input.MouseButton;
import javafx.scene.input.TransferMode;
import javafx.scene.shape.SVGPath;
import javafx.scene.layout.Priority;
import javafx.scene.layout.StackPane;
import javafx.scene.layout.VBox;
import pt.yaku.context.ApplicationContext;
import pt.yaku.favorites.Favorites;
import pt.yaku.favorites.TagGroup;
import pt.yaku.favorites.TagGroups;
import pt.yaku.i18n.Messages;
import pt.yaku.library.Library;
import pt.yaku.user.config.BooruConfiguration;
import lombok.Getter;

public class Sidebar {

    public interface SearchHandler {
        void search(BooruConfiguration booru, String tags);
    }

    private static final int SUGGESTION_LIMIT = 10;

    private final ApplicationContext context;
    @Getter
    private final VBox searchNode = new VBox(10);
    @Getter
    private final VBox favoriteTagsNode = new VBox(10);
    private final ListView<BooruConfiguration> booruList = new ListView<>();
    private final TextField searchField = new TextField();
    private final Button searchButton = new Button(Messages.get("common.search"));
    private volatile BooruConfiguration currentBooru;

    public Sidebar(ApplicationContext context, Library library, SearchHandler handler) {
        this.context = context;
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
        boorusTitle.getStyleClass().add("section-title");
        searchNode.getChildren().addAll(boorusTitle,
                booruList, searchField, searchButton);

        buildFavoriteTags(library, handler);

        reload();
    }

    private record Entry(String label, TagGroup group) {
        boolean isGroup() {
            return group != null;
        }
    }

    private void buildFavoriteTags(Library library, SearchHandler handler) {
        Favorites favorites = library.favorites();
        TagGroups tagGroups = library.tagGroups();

        TreeView<Entry> tree = new TreeView<>();
        tree.setShowRoot(false);
        Label placeholder = new Label(Messages.get("sidebar.noFavTags"));
        placeholder.setMouseTransparent(true);
        StackPane treeBox = new StackPane(tree, placeholder);
        tree.setCellFactory(view -> new TagCell(tagGroups));
        VBox.setVgrow(treeBox, Priority.ALWAYS);

        Set<String> expanded = new HashSet<>();
        Runnable rebuild = () -> {
            TreeItem<Entry> root = new TreeItem<>();
            for (TagGroup group : tagGroups.getGroups()) {
                TreeItem<Entry> groupItem = new TreeItem<>(new Entry(group.getName(), group));
                SVGPath icon = folderIcon();
                groupItem.setGraphic(icon);
                groupItem.setExpanded(expanded.contains(group.getName()));
                markOpen(icon, groupItem.isExpanded());
                groupItem.expandedProperty().addListener((obs, was, is) -> {
                    markOpen(icon, is);
                    if (is) {
                        expanded.add(group.getName());
                    } else {
                        expanded.remove(group.getName());
                    }
                });
                group.getTags().forEach(tag -> groupItem.getChildren().add(new TreeItem<>(new Entry(tag, null))));
                root.getChildren().add(groupItem);
            }
            for (String tag : favorites.getTags()) {
                if (!tagGroups.isGrouped(tag)) {
                    root.getChildren().add(new TreeItem<>(new Entry(tag, null)));
                }
            }
            tree.setRoot(root);
            placeholder.setVisible(root.getChildren().isEmpty());
        };
        // Membership lists change independently of the group list, so listen to each group as it appears.
        ListChangeListener<String> membership = change -> Platform.runLater(rebuild);
        tagGroups.getGroups().forEach(group -> group.getTags().addListener(membership));
        tagGroups.getGroups().addListener((ListChangeListener<TagGroup>) change -> {
            while (change.next()) {
                change.getAddedSubList().forEach(group -> group.getTags().addListener(membership));
                change.getRemoved().forEach(group -> group.getTags().removeListener(membership));
            }
            Platform.runLater(rebuild);
        });
        favorites.getTags().addListener((ListChangeListener<String>) change -> Platform.runLater(rebuild));
        rebuild.run();

        tree.setOnMouseClicked(event -> {
            TreeItem<Entry> item = tree.getSelectionModel().getSelectedItem();
            if (event.getButton() == MouseButton.PRIMARY && event.getClickCount() == 2
                    && item != null && !item.getValue().isGroup()) {
                handler.search(currentBooru, item.getValue().label());
            }
        });

        // Dropping a tag that came from a group anywhere outside the groups takes it out of that group.
        tree.setOnDragOver(event -> {
            if (draggedFrom != null && event.getDragboard().hasString()) {
                event.acceptTransferModes(TransferMode.MOVE);
            }
        });
        tree.setOnDragDropped(event -> {
            boolean done = draggedFrom != null && event.getDragboard().hasString();
            if (done) {
                tagGroups.removeTag(draggedFrom, event.getDragboard().getString());
            }
            event.setDropCompleted(done);
            event.consume();
        });

        ContextMenu menu = new ContextMenu();
        menu.setOnShowing(event -> {
            TreeItem<Entry> item = tree.getSelectionModel().getSelectedItem();
            menu.getItems().clear();
            if (item == null) {
                return;
            }
            Entry entry = item.getValue();
            if (entry.isGroup()) {
                menu.getItems().addAll(groupItems(entry.group(), tagGroups, tree, handler));
            } else {
                menu.getItems().addAll(tagItems(entry.label(), item.getParent().getValue(), library, tree, handler));
            }
        });
        tree.setContextMenu(menu);

        Button newGroup = new Button(Messages.get("tagGroups.new"));
        newGroup.setMaxWidth(Double.MAX_VALUE);
        newGroup.setOnAction(event -> TagGroupMenu.askNewGroup(tagGroups, newGroup.getScene().getWindow()));

        Label favTagsTitle = new Label(Messages.get("sidebar.favTags"));
        favTagsTitle.getStyleClass().add("section-title");
        favoriteTagsNode.getChildren().addAll(favTagsTitle, treeBox, newGroup);
    }

    private static final PseudoClass DROP_TARGET = PseudoClass.getPseudoClass("drop-target");
    private static final String FOLDER_SHAPE =
            "M1.5 3.5 a1 1 0 0 1 1 -1 h4 l1.5 1.8 h6 a1 1 0 0 1 1 1 v7.4 a1 1 0 0 1 -1 1 h-11.5 a1 1 0 0 1 -1 -1 z";

    /** The group a dragged sidebar tag came from, or null when the drag started elsewhere. */
    private TagGroup draggedFrom;

    private static SVGPath folderIcon() {
        SVGPath icon = new SVGPath();
        icon.setContent(FOLDER_SHAPE);
        icon.getStyleClass().add("folder-icon");
        return icon;
    }

    private static void markOpen(SVGPath icon, boolean open) {
        icon.getStyleClass().remove("folder-open");
        if (open) {
            icon.getStyleClass().add("folder-open");
        }
    }

    /** Groups look like folders (icon, bold, tinted) and accept dropped tags; plain tags can be dragged. */
    private final class TagCell extends TreeCell<Entry> {

        TagCell(TagGroups tagGroups) {
            setOnDragDetected(event -> {
                Entry entry = getItem();
                if (entry == null || entry.isGroup()) {
                    return;
                }
                Dragboard board = startDragAndDrop(TransferMode.COPY_OR_MOVE);
                ClipboardContent content = new ClipboardContent();
                content.putString(entry.label());
                board.setContent(content);
                Entry parent = getTreeItem().getParent().getValue();
                draggedFrom = parent != null && parent.isGroup() ? parent.group() : null;
                event.consume();
            });
            setOnDragDone(event -> draggedFrom = null);
            setOnDragOver(event -> {
                if (acceptsDrop(event.getDragboard().hasString())) {
                    event.acceptTransferModes(TransferMode.COPY_OR_MOVE);
                    event.consume();
                }
            });
            setOnDragEntered(event -> {
                if (acceptsDrop(event.getDragboard().hasString())) {
                    pseudoClassStateChanged(DROP_TARGET, true);
                }
            });
            setOnDragExited(event -> pseudoClassStateChanged(DROP_TARGET, false));
            setOnDragDropped(event -> {
                boolean done = acceptsDrop(event.getDragboard().hasString());
                if (done) {
                    tagGroups.addTag(getItem().group(), event.getDragboard().getString());
                }
                pseudoClassStateChanged(DROP_TARGET, false);
                event.setDropCompleted(done);
                event.consume();
            });
        }

        private boolean acceptsDrop(boolean hasText) {
            return hasText && getItem() != null && getItem().isGroup();
        }

        @Override
        protected void updateItem(Entry entry, boolean empty) {
            super.updateItem(entry, empty);
            boolean group = !empty && entry != null && entry.isGroup();
            getStyleClass().remove("tag-group-cell");
            if (group) {
                getStyleClass().add("tag-group-cell");
            }
            setText(empty || entry == null ? null
                    : group ? entry.label() + "  (" + entry.group().getTags().size() + ")" : entry.label());
        }
    }

    private List<MenuItem> groupItems(TagGroup group, TagGroups tagGroups, TreeView<Entry> tree,
                                      SearchHandler handler) {
        MenuItem search = new MenuItem(Messages.get("tagGroups.search"));
        search.setDisable(group.getTags().isEmpty());
        search.setOnAction(event -> handler.search(currentBooru, String.join(" ", group.getTags())));
        MenuItem rename = new MenuItem(Messages.get("tagGroups.rename"));
        rename.setOnAction(event -> Dialogs.askText(tree.getScene().getWindow(),
                Messages.get("tagGroups.renameTitle"), Messages.get("tagGroups.name"), group.getName())
                .ifPresent(name -> {
                    if (!tagGroups.rename(group, name)) {
                        Alert alert = Styles.alert(Alert.AlertType.WARNING, Messages.get("tagGroups.exists", name));
                        alert.initOwner(tree.getScene().getWindow());
                        alert.showAndWait();
                    }
                }));
        MenuItem delete = new MenuItem(Messages.get("tagGroups.delete"));
        delete.setOnAction(event -> tagGroups.delete(group));
        return List.of(search, rename, new SeparatorMenuItem(), delete);
    }

    private List<MenuItem> tagItems(String tag, Entry parent, Library library, TreeView<Entry> tree,
                                    SearchHandler handler) {
        List<MenuItem> items = new ArrayList<>();
        MenuItem search = new MenuItem(Messages.get("tagPanel.search"));
        search.setOnAction(event -> handler.search(currentBooru, tag));
        items.add(search);
        items.add(TagGroupMenu.create(library.tagGroups(), tag, () -> tree.getScene().getWindow()));
        if (parent != null && parent.isGroup()) {
            MenuItem remove = new MenuItem(Messages.get("tagGroups.removeFrom"));
            remove.setOnAction(event -> library.tagGroups().removeTag(parent.group(), tag));
            items.add(remove);
        }
        MenuItem unfavorite = new MenuItem(Messages.get("common.unfavorite"));
        unfavorite.setOnAction(event -> library.favorites().toggleTag(tag));
        items.add(new SeparatorMenuItem());
        items.add(unfavorite);
        return items;
    }

    public void focusSearch() {
        searchField.requestFocus();
        searchField.selectAll();
    }

    public void reload() {
        booruList.getItems().setAll(context.getUserConfig().getBooruConfigurations());
    }

    private List<String> suggestTags(String prefix) throws Exception {
        BooruConfiguration booru = currentBooru;
        if (booru == null || booru.getEngine() == null) {
            return List.of();
        }
        return context.getRestConfig().clientFor(booru).suggestTags(prefix, SUGGESTION_LIMIT);
    }

    private static String describe(BooruConfiguration booru) {
        return booru.getKindLabel() + " - " + booru.getBaseUrl();
    }
}
