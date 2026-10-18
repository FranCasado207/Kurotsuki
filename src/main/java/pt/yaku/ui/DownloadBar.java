package pt.yaku.ui;

import javafx.beans.binding.Bindings;
import javafx.geometry.Insets;
import javafx.scene.Node;
import javafx.scene.control.Button;
import javafx.scene.control.Label;
import javafx.scene.control.ListCell;
import javafx.scene.control.ListView;
import javafx.scene.control.ProgressBar;
import javafx.scene.layout.HBox;
import javafx.scene.layout.Region;
import javafx.scene.layout.Priority;
import javafx.scene.layout.VBox;
import pt.yaku.download.DownloadItem;
import pt.yaku.download.DownloadItem.Status;
import pt.yaku.download.DownloadQueue;
import pt.yaku.library.Library;
import pt.yaku.i18n.Messages;

public class DownloadBar {

    private final VBox node = new VBox(6);

    public DownloadBar(Library library) {
        DownloadQueue queue = library.queue();

        Label title = new Label();
        title.textProperty().bind(Bindings.createStringBinding(
                () -> Messages.get("download.queueTitle", String.valueOf(queue.getItems().size())), queue.getItems()));

        Button downloadButton = new Button(Messages.get("common.download"));
        downloadButton.setOnAction(event -> queue.startAll());
        downloadButton.disableProperty().bind(Bindings.isEmpty(queue.getItems()));

        Button clearFinished = new Button(Messages.get("download.clearFinished"));
        clearFinished.setOnAction(event -> queue.removeFinished());

        Region spacer = new Region();
        HBox.setHgrow(spacer, Priority.ALWAYS);
        HBox header = new HBox(10, title, spacer, clearFinished, downloadButton);

        ListView<DownloadItem> list = new ListView<>(queue.getItems());
        list.setPlaceholder(new Label(Messages.get("download.empty")));
        list.setCellFactory(view -> new DownloadCell(queue));
        list.setPrefHeight(170);

        node.setPadding(new Insets(8, 10, 10, 10));
        node.getChildren().addAll(header, list);
    }

    public Node getNode() {
        return node;
    }

    private static class DownloadCell extends ListCell<DownloadItem> {

        private final DownloadQueue queue;
        private final Label name = new Label();
        private final ProgressBar progress = new ProgressBar(0);
        private final Label state = new Label();
        private final Button remove = new Button("✕");
        private final HBox row = new HBox(10, name, progress, state, remove);
        private DownloadItem bound;

        DownloadCell(DownloadQueue queue) {
            this.queue = queue;
            name.setMinWidth(0);
            name.setPrefWidth(260);
            progress.setPrefWidth(200);
            state.setMinWidth(180);
            remove.setOnAction(event -> queue.remove(getItem()));
        }

        @Override
        protected void updateItem(DownloadItem item, boolean empty) {
            super.updateItem(item, empty);
            if (bound != null) {
                progress.progressProperty().unbind();
                state.textProperty().unbind();
                bound = null;
            }
            if (empty || item == null) {
                setGraphic(null);
                return;
            }
            name.setText(item.getPost().getId() + " - " + item.getPost().getImage());
            progress.progressProperty().bind(item.progressProperty());
            state.textProperty().bind(item.labelProperty());
            remove.setDisable(item.getStatus() == Status.DOWNLOADING);
            bound = item;
            setGraphic(row);
        }
    }
}
