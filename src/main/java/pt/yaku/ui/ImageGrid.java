package pt.yaku.ui;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.function.Consumer;
import java.util.function.BiConsumer;

import javafx.application.Platform;
import javafx.collections.ListChangeListener;
import javafx.collections.WeakListChangeListener;
import javafx.event.EventTarget;
import javafx.geometry.BoundingBox;
import javafx.geometry.Insets;
import javafx.geometry.Point2D;
import javafx.geometry.Pos;
import javafx.scene.Cursor;
import javafx.scene.Node;
import javafx.scene.control.Button;
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
import javafx.scene.input.MouseEvent;
import javafx.scene.layout.BorderPane;
import javafx.scene.layout.HBox;
import javafx.scene.layout.Priority;
import javafx.scene.layout.Region;
import javafx.scene.layout.StackPane;
import javafx.scene.layout.TilePane;
import javafx.scene.layout.VBox;
import javafx.scene.shape.SVGPath;
import javafx.scene.shape.Rectangle;
import pt.yaku.booru.Post;
import pt.yaku.download.DownloadItem;
import pt.yaku.library.Library;
import pt.yaku.library.PostFolder;
import pt.yaku.i18n.Messages;
import lombok.Getter;

public class ImageGrid {

    private static final int THUMBNAIL_SIZE = 160;
    private static final double LOAD_MORE_THRESHOLD = 0.8;
    private static final double DRAG_THRESHOLD = 4;
    private static final String QUEUED_CLASS = "thumbnail-queued";
    private static final String SELECTED_CLASS = "thumbnail-selected";
    private static final String SEARCH_ICON = "M10,10 m-6,0 a6,6 0 1,0 12,0 a6,6 0 1,0 -12,0 M14.5,14.5 L20,20";
    private static final String CHECK_ICON = "M4,12 L9,17 L20,6";

    private final Library library;
    @Getter
    private final VBox node = new VBox(5);
    private final Label status = new Label(Messages.get("grid.initial"));
    private final Button clearButton = new Button();
    private final TilePane tiles = new TilePane(10, 10);
    private final Rectangle marquee = new Rectangle();
    private final StackPane content = new StackPane(tiles, marquee);
    private final ScrollPane scroll = new ScrollPane(content);
    private final StackPane stateLayer = new StackPane();
    private final StackPane viewport = new StackPane(scroll, stateLayer);
    private final Map<String, Post> posts = new LinkedHashMap<>();
    private final Set<String> selected = new LinkedHashSet<>();
    private Runnable onNearBottom;
    private BiConsumer<Post, List<Post>> onPostClicked;
    private String removeLabel;
    private Consumer<Post> removeAction;

    private Point2D pressPoint;
    private Set<String> selectionBeforeDrag = Set.of();
    private boolean marqueeActive;
    private boolean suppressClick;

    private final ListChangeListener<DownloadItem> queueListener = change -> refreshQueueMarks();

    public ImageGrid(Library library) {
        this.library = library;
        library.queue().getItems().addListener(new WeakListChangeListener<>(queueListener));

        tiles.setPadding(new Insets(10));
        tiles.setPrefColumns(5);

        marquee.setManaged(false);
        marquee.setVisible(false);
        marquee.setMouseTransparent(true);
        marquee.getStyleClass().add("selection-marquee");
        content.setOnMousePressed(this::onPressed);
        content.setOnMouseDragged(this::onDragged);
        content.setOnMouseReleased(this::onReleased);

        scroll.setFitToWidth(true);
        VBox.setVgrow(scroll, Priority.ALWAYS);
        scroll.vvalueProperty().addListener((obs, oldValue, newValue) -> {
            if (newValue.doubleValue() >= LOAD_MORE_THRESHOLD) {
                notifyNearBottom();
            }
        });
        tiles.heightProperty().addListener((obs, oldHeight, newHeight) -> fillViewportIfNeeded());
        scroll.viewportBoundsProperty().addListener((obs, oldBounds, newBounds) -> fillViewportIfNeeded());

        clearButton.setOnAction(event -> clearSelection());
        clearButton.setVisible(false);
        clearButton.setManaged(false);

        Region headerSpacer = new Region();
        HBox.setHgrow(headerSpacer, Priority.ALWAYS);
        HBox header = new HBox(10, status, headerSpacer, clearButton);
        header.setAlignment(Pos.CENTER_LEFT);
        header.setPadding(new Insets(0, 0, 0, 0));

        node.setPadding(new Insets(10, 10, 10, 0));
        stateLayer.setPickOnBounds(false);
        stateLayer.setMouseTransparent(true);
        VBox.setVgrow(viewport, Priority.ALWAYS);
        node.getChildren().addAll(header, viewport);
    }

    public void setOnNearBottom(Runnable onNearBottom) {
        this.onNearBottom = onNearBottom;
    }

    public void setOnPostClicked(BiConsumer<Post, List<Post>> onPostClicked) {
        this.onPostClicked = onPostClicked;
    }

    public void setRemoveAction(String label, Consumer<Post> action) {
        this.removeLabel = label;
        this.removeAction = action;
    }

    public void showLoading() {
        clearTiles();
        status.setText(Messages.get("grid.searching"));
    }

    public void showPosts(List<Post> posts) {
        clearTiles();
        appendPosts(posts);
    }

    public void appendPosts(List<Post> posts) {
        stateLayer.getChildren().clear();
        for (Post post : posts) {
            if (post.getPreviewUrl() != null) {
                this.posts.put(post.key(), post);
                tiles.getChildren().add(thumbnail(post));
            }
        }
        Platform.runLater(this::fillViewportIfNeeded);
    }

    public void showEmptyState(String message) {
        BorderPane layer = new BorderPane(stateCard(message, SEARCH_ICON));
        layer.setMouseTransparent(true);
        stateLayer.getChildren().setAll(layer);
    }

    public void showEndState(String message) {
        BorderPane layer = new BorderPane();
        layer.setBottom(stateCard(message, CHECK_ICON));
        layer.setPadding(new Insets(0, 0, 12, 0));
        layer.setMouseTransparent(true);
        stateLayer.getChildren().setAll(layer);
    }

    private static VBox stateCard(String message, String iconPath) {
        SVGPath icon = new SVGPath();
        icon.setContent(iconPath);
        icon.getStyleClass().add("state-icon");
        icon.setScaleX(2.5);
        icon.setScaleY(2.5);
        Label text = new Label(message);
        text.getStyleClass().add("state-text");
        VBox card = new VBox(22, icon, text);
        card.setAlignment(Pos.CENTER);
        card.setMouseTransparent(true);
        return card;
    }

    public void setStatus(String message) {
        status.setText(message);
    }

    public void showMessage(String message) {
        clearTiles();
        status.setText(message);
    }

    private void clearTiles() {
        tiles.getChildren().clear();
        stateLayer.getChildren().clear();
        posts.clear();
        selected.clear();
        updateClearButton();
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
        setMarkClass(tile, QUEUED_CLASS, queued);
        ((Label) ((StackPane) tile).getChildren().get(1)).setVisible(queued);
    }

    private static void applySelectionMark(Node tile, boolean isSelected) {
        setMarkClass(tile, SELECTED_CLASS, isSelected);
    }

    private static void setMarkClass(Node tile, String styleClass, boolean on) {
        tile.getStyleClass().removeIf(styleClass::equals);
        if (on) {
            tile.getStyleClass().add(styleClass);
        }
    }

    private void refreshSelectionMarks() {
        for (Node tile : tiles.getChildren()) {
            if (tile.getUserData() instanceof String key) {
                applySelectionMark(tile, selected.contains(key));
            }
        }
        updateClearButton();
    }

    private void updateClearButton() {
        boolean any = !selected.isEmpty();
        clearButton.setText(withCount(Messages.get("grid.clearSelection"), selected.size()));
        clearButton.setVisible(any);
        clearButton.setManaged(any);
    }

    private void toggleSelection(String key) {
        if (!selected.remove(key)) {
            selected.add(key);
        }
        refreshSelectionMarks();
    }

    public void selectAll() {
        selected.clear();
        selected.addAll(posts.keySet());
        refreshSelectionMarks();
    }

    private void clearSelection() {
        if (!selected.isEmpty()) {
            selected.clear();
            refreshSelectionMarks();
        }
    }

    private List<Post> selectedPosts() {
        List<Post> result = new ArrayList<>();
        for (String key : selected) {
            Post post = posts.get(key);
            if (post != null) {
                result.add(post);
            }
        }
        return result;
    }

    private void onPressed(MouseEvent event) {
        if (event.getButton() != MouseButton.PRIMARY) {
            return;
        }
        pressPoint = content.sceneToLocal(event.getSceneX(), event.getSceneY());
        selectionBeforeDrag = event.isShortcutDown() ? new LinkedHashSet<>(selected) : Set.of();
        marqueeActive = false;
        suppressClick = false;
    }

    private void onDragged(MouseEvent event) {
        if (pressPoint == null || event.getButton() != MouseButton.PRIMARY) {
            return;
        }
        Point2D current = content.sceneToLocal(event.getSceneX(), event.getSceneY());
        if (!marqueeActive) {
            if (current.distance(pressPoint) < DRAG_THRESHOLD) {
                return;
            }
            marqueeActive = true;
            marquee.setVisible(true);
        }

        double x = Math.min(pressPoint.getX(), current.getX());
        double y = Math.min(pressPoint.getY(), current.getY());
        double width = Math.abs(current.getX() - pressPoint.getX());
        double height = Math.abs(current.getY() - pressPoint.getY());
        marquee.setX(x);
        marquee.setY(y);
        marquee.setWidth(width);
        marquee.setHeight(height);

        BoundingBox area = new BoundingBox(x, y, width, height);
        Set<String> hits = new LinkedHashSet<>(selectionBeforeDrag);
        for (Node tile : tiles.getChildren()) {
            if (tile.getUserData() instanceof String key
                    && area.intersects(content.sceneToLocal(tile.localToScene(tile.getBoundsInLocal())))) {
                hits.add(key);
            }
        }
        selected.clear();
        selected.addAll(hits);
        refreshSelectionMarks();
        event.consume();
    }

    private void onReleased(MouseEvent event) {
        if (pressPoint == null || event.getButton() != MouseButton.PRIMARY) {
            return;
        }
        if (marqueeActive) {
            marquee.setVisible(false);
            suppressClick = true;
            event.consume();
        } else if (!event.isShortcutDown() && !isOnTile(event.getTarget())) {
            clearSelection();
        }
        marqueeActive = false;
        pressPoint = null;
    }

    private static boolean isOnTile(EventTarget target) {
        if (!(target instanceof Node start)) {
            return false;
        }
        for (Node current = start; current != null; current = current.getParent()) {
            if (current.getStyleClass().contains("thumbnail")) {
                return true;
            }
        }
        return false;
    }

    private StackPane thumbnail(Post post) {
        ImageView view = new ImageView();
        view.setFitWidth(THUMBNAIL_SIZE);
        view.setFitHeight(THUMBNAIL_SIZE);
        view.setPreserveRatio(true);

        Label badge = new Label(Messages.get("grid.queuedBadge"));
        badge.getStyleClass().add("queued-badge");
        StackPane.setAlignment(badge, Pos.TOP_LEFT);
        badge.setVisible(false);

        StackPane tile = new StackPane(view, badge);
        tile.getStyleClass().add("thumbnail");
        tile.setCursor(Cursor.HAND);
        tile.setUserData(post.key());
        tile.setOnMouseClicked(event -> {
            if (event.getButton() != MouseButton.PRIMARY || suppressClick) {
                return;
            }
            if (event.isShortcutDown()) {
                toggleSelection(post.key());
            } else {
                clearSelection();
                if (onPostClicked != null) {
                    onPostClicked.accept(post, List.copyOf(posts.values()));
                }
            }
        });
        tile.setOnContextMenuRequested(event -> {
            if (!selected.contains(post.key())) {
                selected.clear();
                selected.add(post.key());
                refreshSelectionMarks();
            }
            buildMenu(selectedPosts(), tile).show(tile, event.getScreenX(), event.getScreenY());
        });
        applyQueueMark(tile, library.queue().contains(post.key()));
        applySelectionMark(tile, selected.contains(post.key()));
        Tooltip.install(tile, new Tooltip("ID " + post.getId() + " - " + post.getRating()
                + "\n" + post.getTags()));

        library.images().load(post.getPreviewUrl(), THUMBNAIL_SIZE).thenAccept(image -> {
            if (image != null) {
                Platform.runLater(() -> view.setImage(image));
            }
        });
        return tile;
    }

    private ContextMenu buildMenu(List<Post> targets, Node tile) {
        int count = targets.size();

        MenuItem favoriteItem = new MenuItem();
        favoriteItem.setOnAction(event -> {
            boolean remove = allFavorite(targets);
            for (Post post : targets) {
                if (library.favorites().isPostFavorite(post.key()) == remove) {
                    library.favorites().togglePost(post);
                }
            }
        });

        Menu folderMenu = new Menu(Messages.get("grid.addToFolder"));

        MenuItem queueItem = new MenuItem();
        queueItem.setOnAction(event -> {
            boolean remove = allQueued(targets);
            for (Post post : targets) {
                if (library.queue().contains(post.key()) == remove) {
                    library.queue().toggle(post);
                }
            }
        });

        ContextMenu menu = new ContextMenu(favoriteItem, folderMenu, new SeparatorMenuItem(), queueItem);
        if (removeAction != null) {
            MenuItem removeItem = new MenuItem(withCount(removeLabel, count));
            removeItem.setOnAction(event -> {
                targets.forEach(removeAction);
                clearSelection();
            });
            menu.getItems().add(removeItem);
        }

        menu.setOnShowing(event -> {
            favoriteItem.setText(withCount(allFavorite(targets)
                    ? Messages.get("common.unfavorite") : Messages.get("common.favorite"), count));
            queueItem.setText(withCount(allQueued(targets)
                    ? Messages.get("grid.removeFromQueue") : Messages.get("grid.addToQueue"), count));
            folderMenu.getItems().setAll(folderItems(targets, tile));
        });
        return menu;
    }

    private boolean allFavorite(List<Post> targets) {
        return targets.stream().allMatch(post -> library.favorites().isPostFavorite(post.key()));
    }

    private boolean allQueued(List<Post> targets) {
        return targets.stream().allMatch(post -> library.queue().contains(post.key()));
    }

    private static String withCount(String text, int count) {
        return count > 1 ? text + " (" + count + ")" : text;
    }

    private List<MenuItem> folderItems(List<Post> targets, Node tile) {
        List<MenuItem> items = new ArrayList<>();
        for (PostFolder folder : library.folders().getFolders()) {
            CheckMenuItem item = new CheckMenuItem(folder.getName());
            item.setSelected(targets.stream().allMatch(post -> folder.contains(post.key())));
            item.setOnAction(event -> {
                boolean add = item.isSelected();
                for (Post post : targets) {
                    if (folder.contains(post.key()) != add) {
                        if (add) {
                            library.folders().addPost(folder, post);
                        } else {
                            library.folders().removePost(folder, post);
                        }
                    }
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
                .ifPresent(name -> {
                    PostFolder folder = library.folders().create(name);
                    targets.forEach(post -> library.folders().addPost(folder, post));
                }));
        items.add(newFolder);
        return items;
    }
}
