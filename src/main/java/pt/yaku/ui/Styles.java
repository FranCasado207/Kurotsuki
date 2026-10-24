package pt.yaku.ui;

import javafx.scene.control.Alert;
import javafx.scene.control.ButtonType;
import javafx.scene.control.DialogPane;
import javafx.scene.Scene;

public final class Styles {

    private static final String APP_CSS = "/style.css";

    private Styles() {
    }

    public static void apply(Scene scene) {
        scene.getStylesheets().add(css());
    }

    public static void apply(DialogPane pane) {
        pane.getStylesheets().add(css());
    }

    public static Alert alert(Alert.AlertType type, String message, ButtonType... buttons) {
        Alert alert = new Alert(type, message, buttons);
        apply(alert.getDialogPane());
        return alert;
    }

    private static String css() {
        return Styles.class.getResource(APP_CSS).toExternalForm();
    }
}
