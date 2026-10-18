package pt.yaku.ui;

import java.util.List;

import javafx.animation.PauseTransition;
import javafx.concurrent.Task;
import javafx.geometry.Side;
import javafx.scene.control.ContextMenu;
import javafx.scene.control.MenuItem;
import javafx.scene.control.TextField;
import javafx.util.Duration;

public class TagAutocomplete {

    public interface Suggester {
        List<String> suggest(String prefix) throws Exception;
    }

    private static final int MIN_PREFIX_LENGTH = 2;

    private final TextField field;
    private final Suggester suggester;
    private final ContextMenu popup = new ContextMenu();
    private final PauseTransition debounce = new PauseTransition(Duration.millis(300));
    private int requestId;

    public TagAutocomplete(TextField field, Suggester suggester) {
        this.field = field;
        this.suggester = suggester;

        debounce.setOnFinished(event -> refresh());
        field.textProperty().addListener((obs, oldText, newText) -> debounce.playFromStart());
    }

    private void refresh() {
        String prefix = currentToken();
        if (prefix.length() < MIN_PREFIX_LENGTH) {
            popup.hide();
            return;
        }

        int id = ++requestId;
        Task<List<String>> task = new Task<>() {
            @Override
            protected List<String> call() throws Exception {
                return suggester.suggest(prefix);
            }
        };
        task.setOnSucceeded(event -> {
            if (id == requestId) {
                show(task.getValue());
            }
        });
        task.setOnFailed(event -> popup.hide());

        Thread thread = new Thread(task, "tag-autocomplete");
        thread.setDaemon(true);
        thread.start();
    }

    private void show(List<String> names) {
        popup.getItems().clear();
        for (String name : names) {
            MenuItem item = new MenuItem(name);
            item.setOnAction(event -> complete(name));
            popup.getItems().add(item);
        }
        if (popup.getItems().isEmpty()) {
            popup.hide();
        } else if (!popup.isShowing()) {
            popup.show(field, Side.BOTTOM, 0, 0);
        }
    }

    private void complete(String name) {
        int caret = field.getCaretPosition();
        int start = caret - currentToken().length();
        field.replaceText(start, caret, name + " ");
        popup.hide();
    }

    private String currentToken() {
        String beforeCaret = field.getText(0, field.getCaretPosition());
        return beforeCaret.substring(beforeCaret.lastIndexOf(' ') + 1);
    }
}
