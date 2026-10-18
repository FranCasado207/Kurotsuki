package pt.yaku.ui;

import javafx.application.Platform;
import javafx.beans.property.BooleanProperty;
import javafx.scene.control.CheckMenuItem;
import javafx.scene.control.Menu;
import javafx.scene.control.MenuBar;
import javafx.scene.control.MenuItem;
import javafx.scene.input.KeyCode;
import javafx.scene.input.KeyCodeCombination;
import javafx.scene.input.KeyCombination;
import javafx.stage.Stage;
import pt.yaku.context.ApplicationContext;
import pt.yaku.i18n.Messages;

public final class MainMenuBar {

    private MainMenuBar() {
    }

    public static MenuBar create(Stage owner, ApplicationContext context, Runnable onSettingsClosed,
                                 BooleanProperty foldersOpen, BooleanProperty favoritesOpen) {
        MenuItem exit = new MenuItem(Messages.get("menu.exit"));
        exit.setAccelerator(new KeyCodeCombination(KeyCode.Q, KeyCombination.CONTROL_DOWN));
        exit.setOnAction(event -> Platform.exit());

        Menu file = new Menu(Messages.get("menu.file"));
        file.getItems().add(exit);

        MenuItem settings = new MenuItem(Messages.get("menu.settings"));
        settings.setOnAction(event -> {
            new SettingsDialog(owner, context).showAndWait();
            onSettingsClosed.run();
        });

        Menu tools = new Menu(Messages.get("menu.tools"));
        tools.getItems().add(settings);

        CheckMenuItem folders = new CheckMenuItem(Messages.get("menu.folders"));
        folders.selectedProperty().bindBidirectional(foldersOpen);

        CheckMenuItem favorites = new CheckMenuItem(Messages.get("favorites.tab"));
        favorites.selectedProperty().bindBidirectional(favoritesOpen);

        Menu window = new Menu(Messages.get("menu.window"));
        window.getItems().addAll(folders, favorites);

        return new MenuBar(file, tools, window);
    }
}
