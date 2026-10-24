package pt.yaku.ui;

import java.io.File;
import java.io.IOException;
import java.util.ArrayList;
import java.util.List;
import java.util.Objects;

import javafx.beans.property.SimpleStringProperty;
import javafx.collections.FXCollections;
import javafx.collections.ObservableList;
import javafx.geometry.Insets;
import javafx.scene.control.Alert;
import javafx.scene.control.Button;
import javafx.scene.control.ChoiceBox;
import javafx.scene.control.ButtonType;
import javafx.scene.control.Dialog;
import javafx.scene.control.Label;
import javafx.scene.control.Tab;
import javafx.scene.control.TabPane;
import javafx.scene.control.TableColumn;
import javafx.scene.control.ListView;
import javafx.scene.control.TableView;
import javafx.scene.control.TextInputDialog;
import javafx.scene.control.TextField;
import javafx.scene.layout.HBox;
import javafx.scene.layout.VBox;
import javafx.stage.DirectoryChooser;
import javafx.stage.Window;
import pt.yaku.cache.CacheManager;
import pt.yaku.context.ApplicationContext;
import pt.yaku.user.config.BooruConfiguration;
import pt.yaku.user.config.TagBlacklist;
import pt.yaku.i18n.Messages;

public class SettingsDialog extends Dialog<Void> {

    private final Window owner;
    private final ApplicationContext context;
    private final ObservableList<BooruConfiguration> items;
    private final TableView<BooruConfiguration> table = new TableView<>();

    public SettingsDialog(Window owner, ApplicationContext context) {
        this.owner = owner;
        this.context = context;
        Styles.apply(getDialogPane());
        initOwner(owner);
        setTitle(Messages.get("settings.title"));

        items = FXCollections.observableArrayList(context.getUserConfig().getBooruConfigurations());

        TableColumn<BooruConfiguration, String> typeColumn = new TableColumn<>(Messages.get("common.type"));
        typeColumn.setCellValueFactory(cell ->
                new SimpleStringProperty(cell.getValue().getKindLabel()));

        TableColumn<BooruConfiguration, String> urlColumn = new TableColumn<>(Messages.get("common.baseUrl"));
        urlColumn.setCellValueFactory(cell ->
                new SimpleStringProperty(Objects.toString(cell.getValue().getBaseUrl(), "")));
        urlColumn.setPrefWidth(320);

        table.getColumns().add(typeColumn);
        table.getColumns().add(urlColumn);
        table.setItems(items);
        table.setPlaceholder(new Label(Messages.get("settings.noBooru")));
        table.setPrefHeight(250);

        Button addButton = new Button(Messages.get("common.add"));
        Button editButton = new Button(Messages.get("common.edit"));
        Button deleteButton = new Button(Messages.get("common.delete"));

        editButton.disableProperty().bind(table.getSelectionModel().selectedItemProperty().isNull());
        deleteButton.disableProperty().bind(table.getSelectionModel().selectedItemProperty().isNull());

        addButton.setOnAction(event -> addBooru());
        editButton.setOnAction(event -> editBooru());
        deleteButton.setOnAction(event -> deleteBooru());

        HBox buttons = new HBox(10, addButton, editButton, deleteButton);

        TextField folderField = new TextField(Objects.toString(context.getUserConfig().getDownloadFolder(), ""));
        folderField.setPromptText(Messages.get("settings.downloadFolderPrompt"));
        folderField.setEditable(false);
        folderField.setPrefWidth(320);
        Button chooseFolderButton = new Button(Messages.get("common.choose"));
        chooseFolderButton.setOnAction(event -> chooseDownloadFolder(folderField));
        HBox folderRow = new HBox(10, new Label(Messages.get("settings.downloads")), folderField, chooseFolderButton);

        ObservableList<String> blacklist = FXCollections.observableArrayList(
                Objects.requireNonNullElse(context.getUserConfig().getBlacklistedTags(), List.of()));
        ListView<String> blacklistView = new ListView<>(blacklist);
        blacklistView.setPlaceholder(new Label(Messages.get("settings.noBlacklist")));
        blacklistView.setPrefHeight(120);
        Button addTagButton = new Button(Messages.get("settings.addTag"));
        addTagButton.setOnAction(event -> addBlacklistedTag(blacklist));
        Button removeTagButton = new Button(Messages.get("common.remove"));
        removeTagButton.disableProperty().bind(blacklistView.getSelectionModel().selectedItemProperty().isNull());
        removeTagButton.setOnAction(event -> {
            String selected = blacklistView.getSelectionModel().getSelectedItem();
            if (selected != null) {
                blacklist.remove(selected);
                saveBlacklist(blacklist);
            }
        });
        VBox blacklistBox = new VBox(6,
                new Label(Messages.get("settings.blacklistLabel")),
                blacklistView, new HBox(10, addTagButton, removeTagButton));

        Label cacheInfo = new Label();
        Runnable refreshCacheInfo = () -> {
            try {
                cacheInfo.setText(Messages.get("settings.cacheSize", formatSize(context.getCache().sizeBytes())));
            } catch (IOException e) {
                cacheInfo.setText(Messages.get("settings.cacheSizeError", e.getMessage()));
            }
        };
        refreshCacheInfo.run();

        TextField cacheField = new TextField(Objects.toString(context.getUserConfig().getCacheFolder(), ""));
        cacheField.setPromptText(Messages.get("settings.cachePrompt"));
        cacheField.setEditable(false);
        cacheField.setPrefWidth(320);
        Button chooseCacheButton = new Button(Messages.get("common.choose"));
        chooseCacheButton.setOnAction(event -> {
            DirectoryChooser chooser = new DirectoryChooser();
            chooser.setTitle(Messages.get("settings.cacheFolderTitle"));
            File folder = chooser.showDialog(owner);
            if (folder != null) {
                cacheField.setText(folder.getAbsolutePath());
                context.getUserConfig().setCacheFolder(folder.getAbsolutePath());
                persist();
                refreshCacheInfo.run();
            }
        });
        Button clearCacheButton = new Button(Messages.get("settings.clearCache"));
        clearCacheButton.setOnAction(event -> clearCache(refreshCacheInfo));
        VBox cacheBox = new VBox(6,
                new Label(Messages.get("settings.cacheLabel")),
                cacheInfo,
                new HBox(10, new Label(Messages.get("settings.folder")), cacheField, chooseCacheButton),
                clearCacheButton);
        ChoiceBox<String> languageBox = new ChoiceBox<>(FXCollections.observableArrayList("English", "Português"));
        languageBox.setValue("pt".equals(context.getUserConfig().getLanguage()) ? "Português" : "English");
        languageBox.setOnAction(event -> {
            context.getUserConfig().setLanguage("Português".equals(languageBox.getValue()) ? "pt" : "en");
            persist();
            Alert notice = Styles.alert(Alert.AlertType.INFORMATION, Messages.get("settings.restartNotice"));
            notice.initOwner(owner);
            notice.showAndWait();
        });
        HBox languageRow = new HBox(10, new Label(Messages.get("settings.language")), languageBox);

        VBox boorusTab = new VBox(10, table, buttons);
        VBox generalTab = new VBox(12, languageRow, folderRow);
        TabPane tabs = new TabPane(
                tab("settings.tabGeneral", generalTab),
                tab("settings.tabBoorus", boorusTab),
                tab("settings.tabBlacklist", blacklistBox),
                tab("settings.tabCache", cacheBox));
        tabs.setPrefWidth(760);
        VBox content = new VBox(tabs);
        content.setPadding(new Insets(10));

        getDialogPane().setPrefSize(800, 520);
        getDialogPane().setContent(content);
        getDialogPane().getButtonTypes().add(ButtonType.CLOSE);
    }

    private static Tab tab(String messageKey, javafx.scene.Node content) {
        VBox body = new VBox(10, content);
        body.setPadding(new Insets(12));
        Tab tab = new Tab(Messages.get(messageKey), body);
        tab.setClosable(false);
        return tab;
    }

    private void addBooru() {
        new BooruEditDialog(owner, null).showAndWait().ifPresent(booru -> {
            items.add(booru);
            save();
        });
    }

    private void editBooru() {
        int index = table.getSelectionModel().getSelectedIndex();
        BooruConfiguration selected = table.getSelectionModel().getSelectedItem();
        if (selected == null) {
            return;
        }
        new BooruEditDialog(owner, selected).showAndWait().ifPresent(booru -> {
            items.set(index, booru);
            save();
        });
    }

    private void deleteBooru() {
        BooruConfiguration selected = table.getSelectionModel().getSelectedItem();
        if (selected == null) {
            return;
        }
        Alert confirm = Styles.alert(Alert.AlertType.CONFIRMATION,
                Messages.get("settings.confirmDeleteBooru"), ButtonType.YES, ButtonType.NO);
        confirm.initOwner(owner);
        confirm.showAndWait()
                .filter(button -> button == ButtonType.YES)
                .ifPresent(button -> {
                    items.remove(selected);
                    save();
                });
    }

    private void chooseDownloadFolder(TextField folderField) {
        DirectoryChooser chooser = new DirectoryChooser();
        chooser.setTitle(Messages.get("settings.downloadFolderTitle"));
        File folder = chooser.showDialog(owner);
        if (folder != null) {
            folderField.setText(folder.getAbsolutePath());
            context.getUserConfig().setDownloadFolder(folder.getAbsolutePath());
            persist();
        }
    }

    private void clearCache(Runnable onCleared) {
        Alert confirm = Styles.alert(Alert.AlertType.CONFIRMATION,
                Messages.get("settings.confirmClearCache"),
                ButtonType.YES, ButtonType.NO);
        confirm.initOwner(owner);
        confirm.showAndWait()
                .filter(button -> button == ButtonType.YES)
                .ifPresent(button -> {
                    try {
                        int deleted = context.getCache().clear();
                        Alert done = Styles.alert(Alert.AlertType.INFORMATION, Messages.get("settings.cacheCleared", String.valueOf(deleted)));
                        done.initOwner(owner);
                        done.showAndWait();
                    } catch (IOException e) {
                        Alert error = Styles.alert(Alert.AlertType.ERROR, Messages.get("settings.clearCacheError", e.getMessage()));
                        error.initOwner(owner);
                        error.showAndWait();
                    }
                    onCleared.run();
                });
    }

    private static String formatSize(long bytes) {
        if (bytes < 1024) {
            return bytes + " B";
        }
        if (bytes < 1024 * 1024) {
            return String.format("%.1f KB", bytes / 1024.0);
        }
        return String.format("%.1f MB", bytes / (1024.0 * 1024.0));
    }

    private void addBlacklistedTag(ObservableList<String> blacklist) {
        TextInputDialog dialog = new TextInputDialog();
        dialog.initOwner(owner);
        dialog.setTitle(Messages.get("settings.blockTagTitle"));
        dialog.setHeaderText(Messages.get("settings.blockTagHeader"));
        dialog.showAndWait()
                .map(TagBlacklist::normalize)
                .filter(tag -> !tag.isBlank() && !blacklist.contains(tag))
                .ifPresent(tag -> {
                    blacklist.add(tag);
                    saveBlacklist(blacklist);
                });
    }

    private void saveBlacklist(ObservableList<String> blacklist) {
        context.getUserConfig().setBlacklistedTags(new ArrayList<>(blacklist));
        persist();
    }

    private void save() {
        context.getUserConfig().setBooruConfigurations(new ArrayList<>(items));
        persist();
    }

    private void persist() {
        try {
            context.saveUserConfig();
        } catch (IOException e) {
            Alert error = Styles.alert(Alert.AlertType.ERROR, Messages.get("common.saveError", e.getMessage()));
            error.initOwner(owner);
            error.showAndWait();
        }
    }
}
