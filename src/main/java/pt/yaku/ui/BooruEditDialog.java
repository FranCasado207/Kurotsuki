package pt.yaku.ui;

import java.util.Objects;

import javafx.beans.binding.Bindings;
import javafx.geometry.Insets;
import javafx.scene.Node;
import javafx.scene.control.ButtonType;
import javafx.scene.control.ComboBox;
import javafx.scene.control.Dialog;
import javafx.scene.control.Label;
import javafx.scene.control.TextField;
import javafx.scene.layout.GridPane;
import javafx.stage.Window;
import pt.yaku.user.config.BooruConfiguration;
import pt.yaku.user.config.BooruType;
import pt.yaku.user.config.preconfigured.DanbooruConfiguration;
import pt.yaku.user.config.preconfigured.GelbooruConfiguration;
import pt.yaku.i18n.Messages;

public class BooruEditDialog extends Dialog<BooruConfiguration> {

    private final ComboBox<BooruType> typeBox = new ComboBox<>();
    private final TextField baseUrlField = new TextField();
    private final TextField apiKeyField = new TextField();
    private final Label apiKeyLabel = new Label(Messages.get("booruEdit.apiKey"));
    private final Label userIdLabel = new Label(Messages.get("booruEdit.userId"));
    private final TextField userIdField = new TextField();

    public BooruEditDialog(Window owner, BooruConfiguration existing) {
        initOwner(owner);
        setTitle(existing == null ? Messages.get("booruEdit.titleNew") : Messages.get("booruEdit.titleEdit"));

        typeBox.getItems().setAll(BooruType.values());
        typeBox.setValue(existing != null ? existing.getBooruType() : BooruType.GENERIC);
        typeBox.valueProperty().addListener((obs, oldType, newType) -> onTypeChanged(newType));

        if (existing != null) {
            baseUrlField.setText(Objects.toString(existing.getCustomBaseUrl(), ""));
            apiKeyField.setText(Objects.toString(existing.getApiKey(), ""));
            if (existing instanceof DanbooruConfiguration danbooru) {
                userIdField.setText(Objects.toString(danbooru.getLogin(), ""));
            } else if (existing instanceof GelbooruConfiguration gelbooru) {
                userIdField.setText(Objects.toString(gelbooru.getUserId(), ""));
            }
        }
        onTypeChanged(typeBox.getValue());

        GridPane grid = new GridPane();
        grid.setHgap(10);
        grid.setVgap(10);
        grid.setPadding(new Insets(15));
        grid.addRow(0, new Label(Messages.get("booruEdit.type")), typeBox);
        grid.addRow(1, new Label(Messages.get("booruEdit.baseUrl")), baseUrlField);
        grid.addRow(2, apiKeyLabel, apiKeyField);
        grid.addRow(3, userIdLabel, userIdField);

        getDialogPane().setContent(grid);
        getDialogPane().getButtonTypes().addAll(ButtonType.OK, ButtonType.CANCEL);

        Node okButton = getDialogPane().lookupButton(ButtonType.OK);
        okButton.disableProperty().bind(Bindings.createBooleanBinding(
                () -> typeBox.getValue() == BooruType.GENERIC && baseUrlField.getText().isBlank(),
                typeBox.valueProperty(), baseUrlField.textProperty()));

        setResultConverter(button -> button == ButtonType.OK ? buildConfiguration() : null);
    }

    private void onTypeChanged(BooruType type) {
        boolean hasUserId = type == BooruType.GELBOORU || type == BooruType.DANBOORU;
        userIdLabel.setText(Messages.get(type == BooruType.DANBOORU ? "booruEdit.login" : "booruEdit.userId"));
        userIdLabel.setVisible(hasUserId);
        userIdLabel.setManaged(hasUserId);
        userIdField.setVisible(hasUserId);
        userIdField.setManaged(hasUserId);
        boolean usesApiKey = type != BooruType.SAFEBOORU;
        apiKeyLabel.setVisible(usesApiKey);
        apiKeyLabel.setManaged(usesApiKey);
        apiKeyField.setVisible(usesApiKey);
        apiKeyField.setManaged(usesApiKey);

        String defaultUrl = type.getDefaultBaseUrl();
        baseUrlField.setPromptText(defaultUrl != null ? defaultUrl : Messages.get("booruEdit.urlRequired"));
    }

    private BooruConfiguration buildConfiguration() {
        BooruType type = typeBox.getValue();
        BooruConfiguration booru;
        if (type == BooruType.GELBOORU) {
            GelbooruConfiguration gelbooru = new GelbooruConfiguration();
            gelbooru.setUserId(blankToNull(userIdField.getText()));
            booru = gelbooru;
        } else if (type == BooruType.DANBOORU) {
            DanbooruConfiguration danbooru = new DanbooruConfiguration();
            danbooru.setLogin(blankToNull(userIdField.getText()));
            booru = danbooru;
        } else {
            booru = new BooruConfiguration();
            booru.setBooruType(type);
        }
        booru.setCustomBaseUrl(blankToNull(baseUrlField.getText()));
        booru.setApiKey(blankToNull(apiKeyField.getText()));
        return booru;
    }

    private static String blankToNull(String text) {
        return text == null || text.isBlank() ? null : text.trim();
    }
}
