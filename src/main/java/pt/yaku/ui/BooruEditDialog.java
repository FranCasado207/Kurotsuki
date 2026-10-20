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
import javafx.util.StringConverter;
import pt.yaku.booru.BooruEngine;
import pt.yaku.booru.BooruFlavor;
import pt.yaku.booru.BooruTemplate;
import pt.yaku.i18n.Messages;
import pt.yaku.user.config.BooruConfiguration;

public class BooruEditDialog extends Dialog<BooruConfiguration> {

    private final ComboBox<BooruTemplate> templateBox = new ComboBox<>();
    private final ComboBox<BooruEngine> engineBox = new ComboBox<>();
    private final ComboBox<BooruFlavor> flavorBox = new ComboBox<>();
    private final TextField baseUrlField = new TextField();
    private final TextField apiKeyField = new TextField();
    private final TextField userIdField = new TextField();
    private final Label engineLabel = new Label(Messages.get("booruEdit.engine"));
    private final Label flavorLabel = new Label(Messages.get("booruEdit.flavor"));
    private final Label apiKeyLabel = new Label(Messages.get("booruEdit.apiKey"));
    private final Label userIdLabel = new Label(Messages.get("booruEdit.userId"));

    public BooruEditDialog(Window owner, BooruConfiguration existing) {
        initOwner(owner);
        setTitle(existing == null ? Messages.get("booruEdit.titleNew") : Messages.get("booruEdit.titleEdit"));

        templateBox.getItems().add(null);
        templateBox.getItems().addAll(BooruTemplate.values());
        templateBox.setConverter(new StringConverter<>() {
            @Override
            public String toString(BooruTemplate template) {
                return template == null ? Messages.get("booruEdit.custom") : template.getLabel();
            }

            @Override
            public BooruTemplate fromString(String text) {
                return null;
            }
        });
        engineBox.getItems().setAll(BooruEngine.values());
        engineBox.setConverter(labelConverter(BooruEngine::getLabel));
        flavorBox.getItems().setAll(BooruFlavor.values());
        flavorBox.setConverter(labelConverter(BooruFlavor::getLabel));

        if (existing != null) {
            templateBox.setValue(existing.getTemplate());
            engineBox.setValue(existing.getEngine());
            flavorBox.setValue(existing.getFlavor());
            baseUrlField.setText(Objects.toString(existing.getCustomBaseUrl(), ""));
            apiKeyField.setText(Objects.toString(existing.getApiKey(), ""));
            userIdField.setText(Objects.toString(existing.getUserId(), ""));
        } else {
            templateBox.setValue(BooruTemplate.GELBOORU);
        }
        templateBox.valueProperty().addListener((obs, oldValue, newValue) -> onChanged());
        engineBox.valueProperty().addListener((obs, oldValue, newValue) -> onChanged());

        GridPane grid = new GridPane();
        grid.setHgap(10);
        grid.setVgap(10);
        grid.setPadding(new Insets(15));
        grid.addRow(0, new Label(Messages.get("booruEdit.template")), templateBox);
        grid.addRow(1, engineLabel, engineBox);
        grid.addRow(2, flavorLabel, flavorBox);
        grid.addRow(3, new Label(Messages.get("booruEdit.baseUrl")), baseUrlField);
        grid.addRow(4, apiKeyLabel, apiKeyField);
        grid.addRow(5, userIdLabel, userIdField);

        getDialogPane().setContent(grid);
        getDialogPane().getButtonTypes().addAll(ButtonType.OK, ButtonType.CANCEL);

        Node okButton = getDialogPane().lookupButton(ButtonType.OK);
        okButton.disableProperty().bind(Bindings.createBooleanBinding(
                () -> templateBox.getValue() == null
                        && (engineBox.getValue() == null || baseUrlField.getText().isBlank()
                            || (engineBox.getValue() == BooruEngine.MOEBOORU_SHIMMIE && flavorBox.getValue() == null)),
                templateBox.valueProperty(), engineBox.valueProperty(), flavorBox.valueProperty(),
                baseUrlField.textProperty()));

        setResultConverter(button -> button == ButtonType.OK ? buildConfiguration() : null);
        onChanged();
    }

    private static <T> StringConverter<T> labelConverter(java.util.function.Function<T, String> label) {
        return new StringConverter<>() {
            @Override
            public String toString(T value) {
                return value == null ? "" : label.apply(value);
            }

            @Override
            public T fromString(String text) {
                return null;
            }
        };
    }

    private BooruEngine effectiveEngine() {
        BooruTemplate template = templateBox.getValue();
        return template != null ? template.getEngine() : engineBox.getValue();
    }

    private boolean usesCredentials() {
        BooruTemplate template = templateBox.getValue();
        if (template != null) {
            return template.isUsesCredentials();
        }
        return engineBox.getValue() != null && engineBox.getValue().isUsesCredentials();
    }

    private void onChanged() {
        boolean custom = templateBox.getValue() == null;
        BooruEngine engine = effectiveEngine();
        setRowVisible(engineLabel, engineBox, custom);
        setRowVisible(flavorLabel, flavorBox, custom && engine == BooruEngine.MOEBOORU_SHIMMIE);

        boolean credentials = usesCredentials();
        setRowVisible(apiKeyLabel, apiKeyField, credentials);
        setRowVisible(userIdLabel, userIdField, credentials);
        apiKeyLabel.setText(Messages.get(engine == BooruEngine.SZURUBOORU ? "booruEdit.token" : "booruEdit.apiKey"));
        userIdLabel.setText(Messages.get(switch (engine == null ? BooruEngine.GELBOORU_FAMILY : engine) {
            case DANBOORU_SOFTWARE, E621 -> "booruEdit.login";
            case SZURUBOORU -> "booruEdit.username";
            default -> "booruEdit.userId";
        }));

        BooruTemplate template = templateBox.getValue();
        String defaultUrl = template != null ? template.getDefaultBaseUrl() : null;
        baseUrlField.setPromptText(defaultUrl != null ? defaultUrl : Messages.get("booruEdit.urlRequired"));
    }

    private static void setRowVisible(Label label, Node field, boolean visible) {
        label.setVisible(visible);
        label.setManaged(visible);
        field.setVisible(visible);
        field.setManaged(visible);
    }

    private BooruConfiguration buildConfiguration() {
        BooruConfiguration booru = new BooruConfiguration();
        BooruTemplate template = templateBox.getValue();
        booru.setTemplate(template);
        if (template == null) {
            booru.setEngine(engineBox.getValue());
            if (engineBox.getValue() == BooruEngine.MOEBOORU_SHIMMIE) {
                booru.setFlavor(flavorBox.getValue());
            }
        }
        booru.setCustomBaseUrl(blankToNull(baseUrlField.getText()));
        boolean credentials = usesCredentials();
        booru.setApiKey(credentials ? blankToNull(apiKeyField.getText()) : null);
        booru.setUserId(credentials ? blankToNull(userIdField.getText()) : null);
        return booru;
    }

    private static String blankToNull(String text) {
        return text == null || text.isBlank() ? null : text.trim();
    }
}
