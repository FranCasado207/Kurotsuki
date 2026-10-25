package pt.yaku.ui;

import java.text.DateFormat;
import java.util.Date;
import java.util.function.Consumer;

import javafx.collections.ListChangeListener;
import javafx.collections.transformation.FilteredList;
import javafx.geometry.Insets;
import javafx.scene.control.ChoiceBox;
import javafx.scene.control.ContextMenu;
import javafx.scene.control.Label;
import javafx.scene.control.ListCell;
import javafx.scene.control.ListView;
import javafx.scene.control.MenuItem;
import javafx.scene.control.ProgressBar;
import javafx.scene.control.Tab;
import javafx.scene.input.MouseButton;
import javafx.scene.layout.BorderPane;
import javafx.scene.layout.HBox;
import javafx.scene.layout.Priority;
import javafx.scene.layout.VBox;
import javafx.util.StringConverter;
import pt.yaku.i18n.Messages;
import pt.yaku.library.Library;
import pt.yaku.library.PoolReading;
import pt.yaku.storage.LibraryStore.ReadingRecord;

/** Pools the user has opened in the reader: what is in progress, with a way to continue, and what is finished. */
public class ReadingTab extends Tab {

    private enum Filter { READING, FINISHED, ALL }

    private final PoolReading reading;
    private final ListView<ReadingRecord> list = new ListView<>();
    private final FilteredList<ReadingRecord> visible;
    private final ChoiceBox<Filter> filterBox = new ChoiceBox<>();

    public ReadingTab(Library library, Consumer<ReadingRecord> onContinue) {
        this.reading = library.reading();
        setText(Messages.get("reading.tab"));

        visible = new FilteredList<>(reading.getEntries(), record -> !record.finished());
        list.setItems(visible);
        list.setPlaceholder(new Label(Messages.get("reading.empty")));
        list.setCellFactory(view -> new RecordCell());
        list.setOnMouseClicked(event -> {
            ReadingRecord record = list.getSelectionModel().getSelectedItem();
            if (event.getButton() == MouseButton.PRIMARY && event.getClickCount() == 2 && record != null) {
                onContinue.accept(record);
            }
        });

        filterBox.getItems().setAll(Filter.values());
        filterBox.setConverter(new StringConverter<>() {
            @Override
            public String toString(Filter filter) {
                return filter == null ? "" : Messages.get("reading.filter." + filter.name().toLowerCase());
            }

            @Override
            public Filter fromString(String text) {
                return null;
            }
        });
        filterBox.setValue(Filter.READING);
        filterBox.valueProperty().addListener((obs, was, is) -> visible.setPredicate(record -> switch (is) {
            case READING -> !record.finished();
            case FINISHED -> record.finished();
            case ALL -> true;
        }));

        MenuItem continueItem = new MenuItem(Messages.get("reading.continue"));
        continueItem.setOnAction(event -> selected(onContinue));
        MenuItem toggleItem = new MenuItem();
        toggleItem.setOnAction(event -> selected(record -> reading.setFinished(record, !record.finished())));
        MenuItem forgetItem = new MenuItem(Messages.get("reading.forget"));
        forgetItem.setOnAction(event -> selected(record -> reading.remove(record.poolKey())));
        ContextMenu menu = new ContextMenu(continueItem, toggleItem, forgetItem);
        menu.setOnShowing(event -> {
            ReadingRecord record = list.getSelectionModel().getSelectedItem();
            menu.getItems().forEach(item -> item.setDisable(record == null));
            toggleItem.setText(Messages.get(record != null && record.finished()
                    ? "reading.markReading" : "reading.markFinished"));
        });
        list.setContextMenu(menu);

        Label title = new Label(Messages.get("reading.title"));
        title.getStyleClass().add("section-title");
        HBox header = new HBox(10, title, filterBox);
        header.setPadding(new Insets(10));
        header.setAlignment(javafx.geometry.Pos.CENTER_LEFT);

        BorderPane content = new BorderPane();
        content.setTop(header);
        content.setCenter(list);
        setContent(content);
    }

    private void selected(Consumer<ReadingRecord> action) {
        ReadingRecord record = list.getSelectionModel().getSelectedItem();
        if (record != null) {
            action.accept(record);
        }
    }

    private static final class RecordCell extends ListCell<ReadingRecord> {

        private final Label name = new Label();
        private final Label meta = new Label();
        private final ProgressBar bar = new ProgressBar(0);
        private final VBox box = new VBox(3, name, bar, meta);

        RecordCell() {
            name.getStyleClass().add("pool-name");
            meta.getStyleClass().add("pool-meta");
            bar.setMaxWidth(Double.MAX_VALUE);
            HBox.setHgrow(box, Priority.ALWAYS);
            box.setPadding(new Insets(4, 2, 4, 2));
        }

        @Override
        protected void updateItem(ReadingRecord record, boolean empty) {
            super.updateItem(record, empty);
            if (empty || record == null) {
                setGraphic(null);
                return;
            }
            name.setText(record.name() == null ? "Pool #" + record.poolId() : record.name().replace('_', ' '));
            bar.setProgress(record.finished() ? 1 : record.total() <= 1 ? 0
                    : (double) record.page() / (record.total() - 1));
            String state = record.finished() ? Messages.get("reading.finishedLabel")
                    : Messages.get("reading.progress", String.valueOf(record.page() + 1),
                            String.valueOf(Math.max(record.total(), record.page() + 1)));
            meta.setText(state + "  ·  " + host(record.booruBaseUrl()) + "  ·  "
                    + DateFormat.getDateInstance(DateFormat.MEDIUM).format(new Date(record.updatedAt())));
            setGraphic(box);
        }

        private static String host(String baseUrl) {
            try {
                String host = java.net.URI.create(baseUrl).getHost();
                return host == null ? baseUrl : host;
            } catch (IllegalArgumentException e) {
                return baseUrl;
            }
        }
    }
}
