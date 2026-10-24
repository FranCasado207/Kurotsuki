package pt.yaku.storage;

import java.util.function.Consumer;

import javafx.application.Platform;
import pt.yaku.i18n.Messages;

/** Shared "update the UI now, write in the background, undo on failure" behaviour of the library models. */
public final class StoreWrites {

    private StoreWrites() {
    }

    public static void persist(LibraryStore store, String errorMessageKey, Consumer<String> onError,
                               LibraryStore.SqlTask write, Runnable undo) {
        store.submit(write, e -> Platform.runLater(() -> {
            undo.run();
            onError.accept(Messages.get(errorMessageKey, e.getMessage()));
        }));
    }
}
