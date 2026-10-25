package pt.yaku.library;

import static org.junit.jupiter.api.Assertions.*;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.stream.Stream;

import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import javafx.application.Platform;
import pt.yaku.booru.Pool;
import pt.yaku.storage.LibraryStore;

public class PoolReadingTest {

    private Path dir;
    private LibraryStore store;

    @BeforeEach
    void setUp() throws Exception {
        dir = Files.createTempDirectory("kurotsuki-reading");
        store = LibraryStore.open(dir.resolve("t.db"));
        try {
            Platform.startup(() -> { });
        } catch (IllegalStateException alreadyRunning) {
            // the toolkit is shared between tests
        }
    }

    @AfterEach
    void tearDown() throws Exception {
        store.close();
        try (Stream<Path> files = Files.walk(dir)) {
            files.sorted(Comparator.reverseOrder()).forEach(p -> p.toFile().delete());
        }
    }

    private static Pool pool(long id, int count) {
        return new Pool(id, "Pool_" + id, "d", "series", count, List.of(), "https://e621.test", null, null);
    }

    private PoolReading reading(List<String> errors) {
        return new PoolReading(store, errors::add);
    }

    @Test
    public void testProgressIsRememberedAndTheLastPageFinishesThePool() {
        PoolReading reading = reading(new ArrayList<>());
        Pool pool = pool(7, 10);
        assertEquals(0, reading.pageOf(pool.key()));
        assertFalse(reading.isFinished(pool.key()));

        reading.record(pool, 4, 10);
        assertEquals(4, reading.pageOf(pool.key()));
        assertFalse(reading.isFinished(pool.key()));

        reading.record(pool, 9, 10);
        assertTrue(reading.isFinished(pool.key()));
        reading.record(pool, 2, 10);
        assertTrue(reading.isFinished(pool.key()), "finished stays until the user resets it");
        assertEquals(2, reading.pageOf(pool.key()));
    }

    @Test
    public void testFinishedCanBeToggledAndForgotten() {
        PoolReading reading = reading(new ArrayList<>());
        Pool pool = pool(8, 20);
        reading.setFinished(pool, true);
        assertTrue(reading.isFinished(pool.key()));
        assertEquals(19, reading.pageOf(pool.key()), "marking an unopened pool finished puts it at the end");
        reading.setFinished(pool, false);
        assertFalse(reading.isFinished(pool.key()));
        reading.remove(pool.key());
        assertNull(reading.get(pool.key()));
        assertTrue(reading.getEntries().isEmpty());
    }

    @Test
    public void testNewestEntryComesFirstAndEverythingIsPersisted() throws Exception {
        PoolReading reading = reading(new ArrayList<>());
        reading.record(pool(1, 5), 1, 5);
        Thread.sleep(5);
        reading.record(pool(2, 5), 2, 5);
        assertEquals(List.of(2L, 1L), reading.getEntries().stream().map(r -> r.poolId()).toList());

        store.close(); // waits for the background writes
        store = LibraryStore.open(dir.resolve("t.db"));
        PoolReading reloaded = reading(new ArrayList<>());
        assertEquals(2, reloaded.getEntries().size());
        assertEquals(2, reloaded.pageOf(pool(2, 5).key()));
    }
}
