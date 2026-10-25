package pt.yaku.library;

import java.sql.SQLException;
import java.util.Comparator;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.function.Consumer;

import javafx.collections.FXCollections;
import javafx.collections.ObservableList;
import lombok.Getter;
import pt.yaku.booru.Pool;
import pt.yaku.i18n.Messages;
import pt.yaku.storage.LibraryStore;
import pt.yaku.storage.LibraryStore.ReadingRecord;
import pt.yaku.storage.StoreWrites;

/**
 * Which pools the user is reading or has finished, and where they stopped. A pool counts as finished once its
 * last page was shown, and stays finished until the user resets it.
 */
public class PoolReading {

    private final LibraryStore store;
    private final Consumer<String> onError;
    @Getter
    private final ObservableList<ReadingRecord> entries = FXCollections.observableArrayList();
    private final Map<String, ReadingRecord> byKey = new HashMap<>();
    private boolean errorShown;

    public PoolReading(LibraryStore store, Consumer<String> onError) {
        this.store = store;
        this.onError = onError;
        try {
            store.readingRecords().forEach(record -> byKey.put(record.poolKey(), record));
        } catch (SQLException e) {
            onError.accept(Messages.get("reading.saveError", e.getMessage()));
        }
        refresh();
    }

    public ReadingRecord get(String poolKey) {
        return byKey.get(poolKey);
    }

    /** The page to resume at; 0 when the pool was never opened. */
    public int pageOf(String poolKey) {
        ReadingRecord record = byKey.get(poolKey);
        return record == null ? 0 : record.page();
    }

    public boolean isFinished(String poolKey) {
        ReadingRecord record = byKey.get(poolKey);
        return record != null && record.finished();
    }

    /** Called as the reader moves through the pool. */
    public void record(Pool pool, int page, int total) {
        ReadingRecord previous = byKey.get(pool.key());
        boolean finished = (previous != null && previous.finished()) || (total > 0 && page >= total - 1);
        ReadingRecord record = previous != null
                ? previous.with(page, total, finished, System.currentTimeMillis())
                : new ReadingRecord(pool.key(), pool.booruBaseUrl(), pool.id(), pool.displayName(), pool.category(),
                        page, total, finished, System.currentTimeMillis());
        put(record);
    }

    /** Marks a pool finished or reading again without touching the page. Creates the entry if needed. */
    public void setFinished(Pool pool, boolean finished) {
        ReadingRecord previous = byKey.get(pool.key());
        ReadingRecord record = previous != null
                ? previous.with(previous.page(), Math.max(previous.total(), pool.postCount()), finished,
                        System.currentTimeMillis())
                : new ReadingRecord(pool.key(), pool.booruBaseUrl(), pool.id(), pool.displayName(), pool.category(),
                        finished ? Math.max(0, pool.postCount() - 1) : 0, pool.postCount(), finished,
                        System.currentTimeMillis());
        put(record);
    }

    public void setFinished(ReadingRecord record, boolean finished) {
        put(record.with(record.page(), record.total(), finished, System.currentTimeMillis()));
    }

    /** Forgets the pool: it is unread again. */
    public void remove(String poolKey) {
        ReadingRecord removed = byKey.remove(poolKey);
        if (removed == null) {
            return;
        }
        refresh();
        StoreWrites.persist(store, "reading.saveError", this::reportOnce, () -> store.deleteReading(poolKey),
                () -> {
                    byKey.put(poolKey, removed);
                    refresh();
                });
    }

    private void put(ReadingRecord record) {
        ReadingRecord previous = byKey.put(record.poolKey(), record);
        refresh();
        StoreWrites.persist(store, "reading.saveError", this::reportOnce, () -> store.saveReading(record), () -> {
            if (previous == null) {
                byKey.remove(record.poolKey());
            } else {
                byKey.put(record.poolKey(), previous);
            }
            refresh();
        });
    }

    /** Progress is saved on every page turn, so a broken database must not open a dialog each time. */
    private void reportOnce(String message) {
        if (!errorShown) {
            errorShown = true;
            onError.accept(message);
        }
    }

    private void refresh() {
        List<ReadingRecord> sorted = byKey.values().stream()
                .sorted(Comparator.comparingLong(ReadingRecord::updatedAt).reversed()).toList();
        entries.setAll(sorted);
    }
}
