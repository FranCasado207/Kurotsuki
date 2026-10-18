package pt.yaku.ui;

import java.util.function.Consumer;

import javafx.geometry.Insets;
import javafx.scene.control.Button;
import javafx.scene.control.Label;
import javafx.scene.control.ListView;
import javafx.scene.control.MenuItem;
import javafx.scene.control.ContextMenu;
import javafx.scene.control.Tab;
import javafx.scene.input.MouseButton;
import javafx.scene.layout.Priority;
import javafx.scene.layout.VBox;
import pt.yaku.library.Library;
import pt.yaku.library.PostFolder;
import pt.yaku.i18n.Messages;

public class FoldersTab extends Tab {

    public FoldersTab(Library library, Consumer<PostFolder> onOpenFolder) {
        setText(Messages.get("folders.tab"));

        ListView<PostFolder> list = new ListView<>(library.folders().getFolders());
        list.setPlaceholder(new Label(Messages.get("folders.empty")));
        VBox.setVgrow(list, Priority.ALWAYS);

        list.setOnMouseClicked(event -> {
            PostFolder folder = list.getSelectionModel().getSelectedItem();
            if (event.getButton() == MouseButton.PRIMARY && event.getClickCount() == 2 && folder != null) {
                onOpenFolder.accept(folder);
            }
        });

        MenuItem open = new MenuItem(Messages.get("folders.open"));
        open.setOnAction(event -> {
            PostFolder folder = list.getSelectionModel().getSelectedItem();
            if (folder != null) {
                onOpenFolder.accept(folder);
            }
        });
        MenuItem delete = new MenuItem(Messages.get("folders.delete"));
        delete.setOnAction(event -> {
            PostFolder folder = list.getSelectionModel().getSelectedItem();
            if (folder != null) {
                library.folders().delete(folder);
            }
        });
        list.setContextMenu(new ContextMenu(open, delete));

        Button newFolder = new Button(Messages.get("folders.new"));
        newFolder.setOnAction(event -> Dialogs.askText(list.getScene().getWindow(), Messages.get("folders.newTitle"), Messages.get("folders.name"))
                .ifPresent(name -> library.folders().create(name)));

        VBox content = new VBox(10, newFolder, list);
        content.setPadding(new Insets(10));
        setContent(content);
    }
}
