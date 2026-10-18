package pt.yaku.ui;

import java.util.Optional;

import javafx.scene.control.TextInputDialog;
import javafx.stage.Window;

public final class Dialogs {

    private Dialogs() {
    }

    public static Optional<String> askText(Window owner, String title, String header) {
        TextInputDialog dialog = new TextInputDialog();
        dialog.initOwner(owner);
        dialog.setTitle(title);
        dialog.setHeaderText(header);
        return dialog.showAndWait()
                .map(String::trim)
                .filter(text -> !text.isEmpty());
    }
}
