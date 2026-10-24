package pt.yaku.ui;

import java.util.ArrayList;
import java.util.List;
import java.util.Map;

import javafx.beans.property.BooleanProperty;
import javafx.beans.property.SimpleBooleanProperty;
import javafx.geometry.Insets;
import javafx.geometry.Pos;
import javafx.scene.Cursor;
import javafx.scene.Node;
import javafx.scene.control.Button;
import javafx.scene.control.Label;
import javafx.scene.control.Tooltip;
import javafx.scene.input.ClipboardContent;
import javafx.scene.input.Dragboard;
import javafx.scene.input.TransferMode;
import javafx.scene.layout.BorderPane;
import javafx.scene.layout.HBox;
import javafx.scene.layout.Pane;
import javafx.scene.layout.Priority;
import javafx.scene.layout.Region;
import javafx.scene.layout.StackPane;
import javafx.scene.layout.VBox;
import pt.yaku.i18n.Messages;
import pt.yaku.layout.Dock;
import pt.yaku.layout.PanelId;
import pt.yaku.layout.PanelLayout;

public class Workspace {

    private static final double DROP_ZONE = 80;
    private static final double MIN_SIZE = 120;
    private static final double MAX_SIZE = 1200;
    private static final double CARD_GAP = 8;

    private final PanelLayout layout;
    private final Map<PanelId, Node> contents;
    private final BorderPane body = new BorderPane();
    private final Region highlight = new Region();
    private final StackPane root = new StackPane(body, highlight);
    private final BooleanProperty editMode = new SimpleBooleanProperty(false);
    private Runnable onChanged = () -> {
    };

    private double resizeStart;
    private double resizeSize;
    private double liveSize;
    private double panelDragStart;
    private double panelDragSize;
    private double panelLiveSize;

    public Workspace(PanelLayout layout, Map<PanelId, Node> contents, Node center) {
        this.layout = layout;
        this.contents = contents;

        body.setPadding(new Insets(CARD_GAP));
        body.setCenter(card(center));
        highlight.setMouseTransparent(true);
        highlight.setVisible(false);
        highlight.getStyleClass().add("drop-highlight");

        root.setOnDragOver(event -> {
            Dock zone = zoneAt(event.getX(), event.getY());
            if (editMode.get() && event.getDragboard().hasString() && zone != null) {
                event.acceptTransferModes(TransferMode.MOVE);
                showHighlight(zone);
            } else {
                highlight.setVisible(false);
            }
            event.consume();
        });
        root.setOnDragDropped(event -> {
            Dock zone = zoneAt(event.getX(), event.getY());
            boolean done = editMode.get() && event.getDragboard().hasString() && zone != null;
            if (done) {
                layout.move(PanelId.valueOf(event.getDragboard().getString()), zone);
                rebuild();
            }
            highlight.setVisible(false);
            event.setDropCompleted(done);
            event.consume();
        });
        root.setOnDragExited(event -> highlight.setVisible(false));

        editMode.addListener((obs, wasEditing, isEditing) -> rebuild());
        rebuild();
    }

    public Node getNode() {
        return root;
    }

    public BooleanProperty editModeProperty() {
        return editMode;
    }

    public void setOnChanged(Runnable onChanged) {
        this.onChanged = onChanged;
    }

    public Dock dockOf(PanelId id) {
        return layout.dockOf(id);
    }

    public void move(PanelId id, Dock dock) {
        layout.move(id, dock);
        rebuild();
    }

    public void reset() {
        layout.reset();
        rebuild();
    }

    public void rebuild() {
        body.setLeft(dockNode(Dock.LEFT));
        body.setRight(dockNode(Dock.RIGHT));
        body.setTop(dockNode(Dock.TOP));
        body.setBottom(dockNode(Dock.BOTTOM));
        onChanged.run();
    }

    private Node dockNode(Dock dock) {
        List<Node> hosts = hostsFor(dock);
        if (hosts.isEmpty()) {
            return null;
        }
        boolean vertical = dock == Dock.LEFT || dock == Dock.RIGHT;
        Pane pane = vertical ? new VBox() : new HBox();
        pane.getChildren().addAll(hosts);
        double size = layout.sizeOf(dock);
        if (vertical) {
            pane.setPrefWidth(size);
        } else {
            pane.setPrefHeight(size);
        }
        Region handle = resizeHandle(dock, pane, vertical);
        return switch (dock) {
            case LEFT -> new HBox(pane, handle);
            case RIGHT -> new HBox(handle, pane);
            case TOP -> new VBox(pane, handle);
            default -> new VBox(handle, pane);
        };
    }

    private Region resizeHandle(Dock dock, Pane pane, boolean vertical) {
        Region handle = new Region();
        handle.getStyleClass().add(vertical ? "resize-handle-vertical" : "resize-handle-horizontal");
        if (vertical) {
            handle.setMinWidth(CARD_GAP);
            handle.setPrefWidth(CARD_GAP);
        } else {
            handle.setMinHeight(CARD_GAP);
            handle.setPrefHeight(CARD_GAP);
        }
        handle.setOnMousePressed(event -> {
            resizeStart = vertical ? event.getSceneX() : event.getSceneY();
            resizeSize = layout.sizeOf(dock);
            liveSize = resizeSize;
            event.consume();
        });
        handle.setOnMouseDragged(event -> {
            double position = vertical ? event.getSceneX() : event.getSceneY();
            double delta = position - resizeStart;
            boolean grows = dock == Dock.LEFT || dock == Dock.TOP;
            liveSize = clamp(resizeSize + (grows ? delta : -delta));
            if (vertical) {
                pane.setPrefWidth(liveSize);
            } else {
                pane.setPrefHeight(liveSize);
            }
            event.consume();
        });
        handle.setOnMouseReleased(event -> {
            layout.setSize(dock, liveSize);
            event.consume();
        });
        handle.setCursor(vertical ? Cursor.H_RESIZE : Cursor.V_RESIZE);
        return handle;
    }

    private static double clamp(double size) {
        return Math.max(MIN_SIZE, Math.min(MAX_SIZE, size));
    }

    private List<Node> hostsFor(Dock dock) {
        boolean vertical = dock == Dock.LEFT || dock == Dock.RIGHT;
        List<Node> hosts = new ArrayList<>();
        Region lastHandle = null;
        for (PanelId id : PanelId.values()) {
            if (layout.dockOf(id) != dock) {
                continue;
            }
            VBox host = host(id);
            Double size = layout.panelSizeOf(id);
            Priority grow = size == null ? Priority.ALWAYS : Priority.NEVER;
            if (size != null) {
                fixSize(host, size, vertical);
            }
            Region handle = panelHandle(id, host, vertical);
            Pane wrapper = vertical ? new VBox(host, handle) : new HBox(host, handle);
            VBox.setVgrow(host, grow);
            HBox.setHgrow(host, grow);
            VBox.setVgrow(wrapper, grow);
            HBox.setHgrow(wrapper, grow);
            hosts.add(wrapper);
            lastHandle = handle;
        }
        if (lastHandle != null) {
            if (vertical) {
                VBox.setMargin(lastHandle, new Insets(0, 0, -CARD_GAP, 0));
            } else {
                HBox.setMargin(lastHandle, new Insets(0, -CARD_GAP, 0, 0));
            }
        }
        return hosts;
    }

    private static void fixSize(Region target, double size, boolean vertical) {
        if (vertical) {
            target.setMinHeight(size);
            target.setPrefHeight(size);
        } else {
            target.setMinWidth(size);
            target.setPrefWidth(size);
        }
    }

    private Region panelHandle(PanelId id, Region target, boolean vertical) {
        Region handle = new Region();
        handle.getStyleClass().add(vertical ? "resize-handle-horizontal" : "resize-handle-vertical");
        if (vertical) {
            handle.setMinHeight(CARD_GAP);
            handle.setPrefHeight(CARD_GAP);
        } else {
            handle.setMinWidth(CARD_GAP);
            handle.setPrefWidth(CARD_GAP);
        }
        handle.setCursor(vertical ? Cursor.V_RESIZE : Cursor.H_RESIZE);
        handle.setOnMousePressed(event -> {
            panelDragStart = vertical ? event.getSceneY() : event.getSceneX();
            panelDragSize = vertical ? target.getHeight() : target.getWidth();
            event.consume();
        });
        handle.setOnMouseDragged(event -> {
            double position = vertical ? event.getSceneY() : event.getSceneX();
            panelLiveSize = clamp(panelDragSize + position - panelDragStart);
            fixSize(target, panelLiveSize, vertical);
            event.consume();
        });
        handle.setOnMouseReleased(event -> {
            layout.setPanelSize(id, panelLiveSize);
            event.consume();
        });
        return handle;
    }

    private static StackPane card(Node content) {
        StackPane card = new StackPane(content);
        card.getStyleClass().add("card");
        return card;
    }

    private VBox host(PanelId id) {
        Label name = new Label(Messages.get(id.labelKey()));
        name.getStyleClass().add("panel-title");
        Button hide = new Button("✕");
        hide.getStyleClass().add("panel-close");
        hide.setTooltip(new Tooltip(Messages.get("layout.hide")));
        hide.setOnAction(event -> move(id, Dock.HIDDEN));

        Region spacer = new Region();
        HBox.setHgrow(spacer, Priority.ALWAYS);
        HBox header = new HBox(8, name, spacer, hide);
        header.setAlignment(Pos.CENTER_LEFT);
        header.getStyleClass().add("panel-header");
        header.setCursor(Cursor.MOVE);
        header.setOnDragDetected(event -> {
            Dragboard dragboard = header.startDragAndDrop(TransferMode.MOVE);
            ClipboardContent content = new ClipboardContent();
            content.putString(id.name());
            dragboard.setContent(content);
            event.consume();
        });
        header.setVisible(editMode.get());
        header.setManaged(editMode.get());

        VBox panelBody = new VBox(contents.get(id));
        panelBody.getStyleClass().add("panel-body");
        VBox.setVgrow(contents.get(id), Priority.ALWAYS);
        VBox.setVgrow(panelBody, Priority.ALWAYS);

        VBox host = new VBox(header, panelBody);
        host.getStyleClass().add("panel-host");
        return host;
    }

    private Dock zoneAt(double x, double y) {
        double width = root.getWidth();
        double height = root.getHeight();
        if (x < DROP_ZONE) {
            return Dock.LEFT;
        }
        if (x > width - DROP_ZONE) {
            return Dock.RIGHT;
        }
        if (y < DROP_ZONE) {
            return Dock.TOP;
        }
        if (y > height - DROP_ZONE) {
            return Dock.BOTTOM;
        }
        return null;
    }

    private void showHighlight(Dock zone) {
        highlight.setVisible(true);
        switch (zone) {
            case LEFT -> {
                highlight.setMaxWidth(DROP_ZONE);
                highlight.setMaxHeight(Double.MAX_VALUE);
                StackPane.setAlignment(highlight, Pos.CENTER_LEFT);
            }
            case RIGHT -> {
                highlight.setMaxWidth(DROP_ZONE);
                highlight.setMaxHeight(Double.MAX_VALUE);
                StackPane.setAlignment(highlight, Pos.CENTER_RIGHT);
            }
            case TOP -> {
                highlight.setMaxWidth(Double.MAX_VALUE);
                highlight.setMaxHeight(DROP_ZONE);
                StackPane.setAlignment(highlight, Pos.TOP_CENTER);
            }
            default -> {
                highlight.setMaxWidth(Double.MAX_VALUE);
                highlight.setMaxHeight(DROP_ZONE);
                StackPane.setAlignment(highlight, Pos.BOTTOM_CENTER);
            }
        }
    }
}
