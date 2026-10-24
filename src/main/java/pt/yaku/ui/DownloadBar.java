package pt.yaku.ui;

import javafx.beans.binding.Bindings;
import javafx.beans.value.ChangeListener;
import javafx.geometry.Insets;
import javafx.geometry.Pos;
import javafx.scene.control.Button;
import javafx.scene.control.Label;
import javafx.scene.control.ListCell;
import javafx.scene.control.ListView;
import javafx.scene.control.ProgressBar;
import javafx.scene.control.Tooltip;
import javafx.scene.layout.HBox;
import javafx.scene.layout.Region;
import javafx.scene.layout.Priority;
import javafx.scene.layout.VBox;
import pt.yaku.download.DownloadItem;
import pt.yaku.download.DownloadItem.Status;
import pt.yaku.download.DownloadQueue;
import pt.yaku.library.Library;
import pt.yaku.i18n.Messages;
import lombok.Getter;

public class DownloadBar {

    private static final String STATE_CLASS_PREFIX = "download-state-";

    @Getter
    private final VBox node = new VBox(8);

    public DownloadBar(Library library) {
        DownloadQueue queue = library.queue();

        Label title = new Label();
        title.getStyleClass().add("section-title");
        title.textProperty().bind(Bindings.createStringBinding(
                () -> Messages.get("download.queueTitle", String.valueOf(queue.getItems().size())), queue.getItems()));

        Button downloadButton = new Button(Messages.get("common.download"));
        downloadButton.getStyleClass().add("accent");
        downloadButton.setOnAction(event -> queue.startAll());
        downloadButton.disableProperty().bind(Bindings.isEmpty(queue.getItems()));

        Button clearFinished = new Button(Messages.get("download.clearFinished"));
        clearFinished.setOnAction(event -> queue.removeFinished());

        Region spacer = new Region();
        HBox.setHgrow(spacer, Priority.ALWAYS);
        HBox header = new HBox(10, title, spacer, clearFinished, downloadButton);
        header.setAlignment(Pos.CENTER_LEFT);

        Label empty = new Label(Messages.get("download.empty"));
        empty.getStyleClass().add("muted-text");

        ListView<DownloadItem> list = new ListView<>(queue.getItems());
        list.setPlaceholder(empty);
        list.setCellFactory(view -> new DownloadCell(queue));
        VBox.setVgrow(list, Priority.ALWAYS);

        node.setPadding(new Insets(12));
        node.getChildren().addAll(header, list);
    }

    private static class DownloadCell extends ListCell<DownloadItem> {

        private static final double PROGRESS_WIDTH = 160;
        private static final double STATE_WIDTH = 170;

        private final Label name = new Label();
        private final ProgressBar progress = new ProgressBar(0);
        private final Label state = new Label();
        private final Button remove = new Button("✕");
        private final HBox row = new HBox(12, name, progress, state, remove);
        private DownloadItem bound;
        private ChangeListener<Status> statusListener;

        DownloadCell(DownloadQueue queue) {
            row.setAlignment(Pos.CENTER_LEFT);
            row.getStyleClass().add("download-row");
            name.setMinWidth(0);
            name.setMaxWidth(Double.MAX_VALUE);
            name.getStyleClass().add("download-name");
            HBox.setHgrow(name, Priority.ALWAYS);
            progress.setPrefWidth(PROGRESS_WIDTH);
            progress.setMinWidth(PROGRESS_WIDTH);
            progress.setMaxWidth(PROGRESS_WIDTH);
            state.setMinWidth(STATE_WIDTH);
            state.setPrefWidth(STATE_WIDTH);
            state.getStyleClass().add("download-state");
            remove.getStyleClass().add("icon-button");
            remove.setTooltip(new Tooltip(Messages.get("common.remove")));
            remove.setOnAction(event -> queue.remove(getItem()));
        }

        @Override
        protected void updateItem(DownloadItem item, boolean empty) {
            super.updateItem(item, empty);
            unbindItem();
            if (empty || item == null) {
                setGraphic(null);
                return;
            }
            name.setText(item.getPost().getId() + " - " + item.getPost().getImage());
            progress.progressProperty().bind(item.progressProperty());
            state.textProperty().bind(item.labelProperty());
            statusListener = (obs, oldStatus, newStatus) -> applyStatus(newStatus);
            item.statusProperty().addListener(statusListener);
            applyStatus(item.getStatus());
            bound = item;
            setGraphic(row);
        }

        private void unbindItem() {
            if (bound != null) {
                progress.progressProperty().unbind();
                state.textProperty().unbind();
                bound.statusProperty().removeListener(statusListener);
                bound = null;
            }
        }

        private void applyStatus(Status status) {
            state.getStyleClass().removeIf(styleClass -> styleClass.startsWith(STATE_CLASS_PREFIX));
            state.getStyleClass().add(STATE_CLASS_PREFIX + status.name().toLowerCase());
            remove.setDisable(status == Status.DOWNLOADING);
        }
    }
}
