package pt.yaku.ui;

import java.util.List;
import java.util.function.Consumer;

import javafx.application.Platform;
import javafx.collections.ListChangeListener;
import javafx.collections.WeakListChangeListener;
import javafx.geometry.Insets;
import javafx.geometry.Pos;
import javafx.scene.Cursor;
import javafx.scene.Node;
import javafx.scene.control.CheckMenuItem;
import javafx.scene.control.ContextMenu;
import javafx.scene.control.Label;
import javafx.scene.control.Menu;
import javafx.scene.control.MenuItem;
import javafx.scene.control.ScrollPane;
import javafx.scene.control.SeparatorMenuItem;
import javafx.scene.control.Tooltip;
import javafx.scene.image.ImageView;
import javafx.scene.input.MouseButton;
import javafx.scene.layout.Priority;
import javafx.scene.layout.StackPane;
import javafx.scene.layout.TilePane;
import javafx.scene.layout.VBox;
import pt.yaku.booru.Post;
import pt.yaku.download.DownloadItem;
import pt.yaku.library.Library;
import pt.yaku.library.PostFolder;
import pt.yaku.i18n.Messages;
import lombok.Getter;

public class ImageGrid {

    private static final int THUMBNAIL_SIZE = 160;
    private static final double LOAD_MORE_THRESHOLD = 0.8;
    private static final String QUEUED_BORDER = "-fx-border-color: #3a7bd5; -fx-border-width: 3;";

    private final Library library;
    @Getter
    private final VBox node = new VBox(5);
    private final Label status = new Label(Messages.get("grid.initial"));
    private final TilePane tiles = new TilePane(10, 10);
    private final ScrollPane scroll = new ScrollPane(tiles);
    private Runnable onNearBottom;
    private Consumer<Post> onPostClicked;
    private String removeLabel;
    private Consumer<Post> removeAction;

    private final ListChangeListener<DownloadItem> queueListener = change -> refreshQueueMarks();

    public ImageGrid(Library library) {
        this.library = library;
        library.queue().getItems().addListener(new WeakListChangeListener<>(queueListener));

        tiles.setPadding(new Insets(10));
        tiles.setPrefColumns(5);

        scroll.setFitToWidth(true);
        VBox.setVgrow(scroll, Priority.ALWAYS);
        scroll.vvalueProperty().addListener((obs, oldValue, newValue) -> {
            if (newValue.doubleValue() >= LOAD_MORE_THRESHOLD) {
                notifyNearBottom();
            }
        });
        tiles.heightProperty().addListener((obs, oldHeight, newHeight) -> fillViewportIfNeeded());
        scroll.viewportBoundsProperty().addListener((obs, oldBounds, newBounds) -> fillViewportIfNeeded());

        node.setPadding(new Insets(10, 10, 10, 0));
        node.getChildren().addAll(status, scroll);
    }


    public void setOnNearBottom(Runnable onNearBottom) {
        this.onNearBottom = onNearBottom;
    }

    public void setOnPostClicked(Consumer<Post> onPostClicked) {
        this.onPostClicked = onPostClicked;
    }

    public void setRemoveAction(String label, Consumer<Post> action) {
        this.removeLabel = label;
        this.removeAction = action;
    }

    public void showLoading() {
        tiles.getChildren().clear();
        status.setText(Messages.get("grid.searching"));
    }

    public void showPosts(List<Post> posts) {
        tiles.getChildren().clear();
        appendPosts(posts);
    }

    public void appendPosts(List<Post> posts) {
        for (Post post : posts) {
            if (post.getPreviewUrl() != null) {
                tiles.getChildren().add(thumbnail(post));
            }
        }
        Platform.runLater(this::fillViewportIfNeeded);
    }

    public void setStatus(String message) {
        status.setText(message);
    }

    public void showMessage(String message) {
        tiles.getChildren().clear();
        status.setText(message);
    }

    private void fillViewportIfNeeded() {
        double viewportHeight = scroll.getViewportBounds().getHeight();
        double contentHeight = tiles.prefHeight(-1);
        if (viewportHeight > 0 && contentHeight <= viewportHeight) {
            notifyNearBottom();
        }
    }

    private void notifyNearBottom() {
        if (onNearBottom != null) {
            onNearBottom.run();
        }
    }

    private void refreshQueueMarks() {
        for (Node tile : tiles.getChildren()) {
            if (tile.getUserData() instanceof String postId) {
                applyQueueMark(tile, library.queue().contains(postId));
            }
        }
    }

    private static void applyQueueMark(Node tile, boolean queued) {
        tile.setStyle(queued ? QUEUED_BORDER : "");
        ((Label) ((StackPane) tile).getChildren().get(1)).setVisible(queued);
    }

    private StackPane thumbnail(Post post) {
        ImageView view = new ImageView();
        view.setFitWidth(THUMBNAIL_SIZE);
        view.setFitHeight(THUMBNAIL_SIZE);
        view.setPreserveRatio(true);

        Label badge = new Label(Messages.get("grid.queuedBadge"));
        badge.setStyle("-fx-background-color: #3a7bd5; -fx-text-fill: white; -fx-padding: 2 6;");
        StackPane.setAlignment(badge, Pos.TOP_LEFT);
        badge.setVisible(false);

        StackPane tile = new StackPane(view, badge);
        tile.setCursor(Cursor.HAND);
        tile.setUserData(post.key());
        tile.setOnMouseClicked(event -> {
            if (event.getButton() == MouseButton.PRIMARY && onPostClicked != null) {
                onPostClicked.accept(post);
            }
        });
        tile.setOnContextMenuRequested(event -> buildMenu(post, tile).show(tile, event.getScreenX(), event.getScreenY()));
        applyQueueMark(tile, library.queue().contains(post.key()));
        Tooltip.install(tile, new Tooltip("ID " + post.getId() + " - " + post.getRating()
                + "\n" + post.getTags()));

        ImageLoader.load(post.getPreviewUrl(), THUMBNAIL_SIZE).thenAccept(image -> {
            if (image != null) {
                Platform.runLater(() -> view.setImage(image));
            }
        });
        return tile;
    }

    private ContextMenu buildMenu(Post post, Node tile) {
        MenuItem favoriteItem = new MenuItem();
        favoriteItem.setOnAction(event -> library.favorites().togglePost(post));

        Menu folderMenu = new Menu(Messages.get("grid.addToFolder"));

        MenuItem queueItem = new MenuItem();
        queueItem.setOnAction(event -> library.queue().toggle(post));

        ContextMenu menu = new ContextMenu(favoriteItem, folderMenu, new SeparatorMenuItem(), queueItem);
        if (removeAction != null) {
            MenuItem removeItem = new MenuItem(removeLabel);
            removeItem.setOnAction(event -> removeAction.accept(post));
            menu.getItems().add(removeItem);
        }

        menu.setOnShowing(event -> {
            favoriteItem.setText(library.favorites().isPostFavorite(post.key())
                    ? Messages.get("common.unfavorite") : Messages.get("common.favorite"));
            queueItem.setText(library.queue().contains(post.key()) ? Messages.get("grid.removeFromQueue") : Messages.get("grid.addToQueue"));
            folderMenu.getItems().setAll(folderItems(post, tile));
        });
        return menu;
    }

    private List<javafx.scene.control.MenuItem> folderItems(Post post, Node tile) {
        List<javafx.scene.control.MenuItem> items = new java.util.ArrayList<>();
        for (PostFolder folder : library.folders().getFolders()) {
            CheckMenuItem item = new CheckMenuItem(folder.getName());
            item.setSelected(folder.contains(post.key()));
            item.setOnAction(event -> {
                if (item.isSelected()) {
                    library.folders().addPost(folder, post);
                } else {
                    library.folders().removePost(folder, post);
                }
            });
            items.add(item);
        }
        if (!items.isEmpty()) {
            items.add(new SeparatorMenuItem());
        }
        MenuItem newFolder = new MenuItem(Messages.get("folders.new"));
        newFolder.setOnAction(event -> Dialogs.askText(tile.getScene().getWindow(),
                        Messages.get("folders.newTitle"), Messages.get("folders.name"))
                .ifPresent(name -> library.folders().addPost(library.folders().create(name), post)));
        items.add(newFolder);
        return items;
    }
}
