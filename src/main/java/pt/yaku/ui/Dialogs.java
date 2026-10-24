package pt.yaku.ui;

import java.util.Optional;

import javafx.scene.control.TextInputDialog;
import javafx.stage.Window;

public final class Dialogs {

    private Dialogs() {
    }

    public static Optional<String> askText(Window owner, String title, String header) {
        return askText(owner, title, header, "");
    }

    public static Optional<String> askText(Window owner, String title, String header, String initial) {
        TextInputDialog dialog = new TextInputDialog(initial);
        dialog.initOwner(owner);
        Styles.apply(dialog.getDialogPane());
        dialog.setTitle(title);
        dialog.setHeaderText(header);
        return dialog.showAndWait()
                .map(String::trim)
                .filter(text -> !text.isEmpty());
    }
}
