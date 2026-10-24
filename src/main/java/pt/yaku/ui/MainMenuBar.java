package pt.yaku.ui;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.InvalidPathException;
import java.nio.file.Path;
import java.util.List;

import javafx.application.Platform;
import javafx.scene.control.Alert;
import javafx.scene.control.Menu;
import javafx.scene.control.MenuBar;
import javafx.scene.control.MenuItem;
import javafx.scene.control.SeparatorMenuItem;
import javafx.scene.input.KeyCode;
import javafx.scene.input.KeyCodeCombination;
import javafx.scene.input.KeyCombination;
import javafx.stage.Stage;
import pt.yaku.context.ApplicationContext;
import pt.yaku.i18n.Messages;
import pt.yaku.user.config.AppPaths;

public final class MainMenuBar {

    private MainMenuBar() {
    }

    public static MenuBar create(Stage owner, ApplicationContext context, Runnable onBoorusChanged,
                                 Runnable onFind, Runnable onSelectAll, List<MenuItem> windowItems) {
        MenuItem exit = new MenuItem(Messages.get("menu.exit"));
        exit.setAccelerator(new KeyCodeCombination(KeyCode.Q, KeyCombination.CONTROL_DOWN));
        exit.setOnAction(event -> Platform.exit());

        Menu file = new Menu(Messages.get("menu.file"));
        file.getItems().add(exit);

        MenuItem find = new MenuItem(Messages.get("menu.find"));
        find.setAccelerator(new KeyCodeCombination(KeyCode.F, KeyCombination.CONTROL_DOWN));
        find.setOnAction(event -> onFind.run());

        MenuItem selectAll = new MenuItem(Messages.get("menu.selectAll"));
        selectAll.setAccelerator(new KeyCodeCombination(KeyCode.A, KeyCombination.CONTROL_DOWN));
        selectAll.setOnAction(event -> onSelectAll.run());

        Menu edit = new Menu(Messages.get("menu.edit"));
        edit.getItems().addAll(find, new SeparatorMenuItem(), selectAll);

        MenuItem settings = new MenuItem(Messages.get("menu.settings"));
        settings.setOnAction(event -> {
            new SettingsDialog(owner, context).showAndWait();
            onBoorusChanged.run();
        });

        MenuItem reload = new MenuItem(Messages.get("menu.reloadBoorus"));
        reload.setAccelerator(new KeyCodeCombination(KeyCode.F5));
        reload.setOnAction(event -> onBoorusChanged.run());

        MenuItem openDownloads = new MenuItem(Messages.get("menu.openDownloads"));
        openDownloads.setOnAction(event -> openDownloadsFolder(owner, context));

        MenuItem openData = new MenuItem(Messages.get("menu.openData"));
        openData.setOnAction(event -> openFolder(owner, AppPaths.get().data()));

        Menu tools = new Menu(Messages.get("menu.tools"));
        tools.getItems().addAll(settings, new SeparatorMenuItem(), reload,
                new SeparatorMenuItem(), openDownloads, openData);

        Menu window = new Menu(Messages.get("menu.window"));
        window.getItems().addAll(windowItems);

        MenuItem about = new MenuItem(Messages.get("menu.about"));
        about.setOnAction(event -> new AboutDialog(owner).showAndWait());

        Menu help = new Menu(Messages.get("menu.help"));
        help.getItems().add(about);

        return new MenuBar(file, edit, tools, window, help);
    }

    private static void openDownloadsFolder(Stage owner, ApplicationContext context) {
        String folder = context.getUserConfig().getDownloadFolder();
        if (folder == null || folder.isBlank()) {
            Alert alert = Styles.alert(Alert.AlertType.WARNING, Messages.get("download.folderNotSet"));
            alert.initOwner(owner);
            alert.showAndWait();
            return;
        }
        openFolder(owner, Path.of(folder));
    }

    private static void openFolder(Stage owner, Path folder) {
        try {
            Files.createDirectories(folder);
            SystemOpener.openFile(folder);
        } catch (IOException | InvalidPathException e) {
            Alert alert = Styles.alert(Alert.AlertType.ERROR, Messages.get("menu.openError", e.getMessage()));
            alert.initOwner(owner);
            alert.showAndWait();
        }
    }
}
