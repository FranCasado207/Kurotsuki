package pt.yaku.ui;

import javafx.geometry.Insets;
import javafx.geometry.Pos;
import javafx.scene.control.ButtonType;
import javafx.scene.control.Dialog;
import javafx.scene.control.Label;
import javafx.scene.image.Image;
import javafx.scene.image.ImageView;
import javafx.scene.layout.HBox;
import javafx.scene.layout.VBox;
import javafx.stage.Window;
import pt.yaku.i18n.Messages;

public class AboutDialog extends Dialog<Void> {

    static final String VERSION = "0.5";

    public AboutDialog(Window owner) {
        initOwner(owner);
        Styles.apply(getDialogPane());
        setTitle(Messages.get("about.title"));

        ImageView icon = new ImageView(new Image(getClass().getResourceAsStream("/icons/kurotsuki-256.png")));
        icon.setFitWidth(96);
        icon.setFitHeight(96);
        icon.setPreserveRatio(true);

        Label name = new Label("Kurotsuki");
        name.getStyleClass().add("section-title");
        Label version = new Label(Messages.get("about.version", VERSION));
        Label description = new Label(Messages.get("about.description"));
        description.setWrapText(true);
        description.setMaxWidth(360);
        Label author = new Label(Messages.get("about.author", "Francisco Casado"));
        author.getStyleClass().add("muted-text");

        VBox text = new VBox(6, name, version, description, author);
        text.setAlignment(Pos.CENTER_LEFT);
        HBox content = new HBox(20, icon, text);
        content.setAlignment(Pos.CENTER_LEFT);
        content.setPadding(new Insets(16));

        getDialogPane().setContent(content);
        getDialogPane().getButtonTypes().add(ButtonType.CLOSE);
    }
}
