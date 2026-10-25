package pt.yaku.ui;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * Keeps the decoded pages of the reader within a memory budget. Pages are dropped least recently used first,
 * except the ones the reader is showing or about to show.
 */
final class PageBudget {

    private final long maxBytes;
    private final Map<Integer, Long> sizes = new LinkedHashMap<>(16, 0.75f, true);
    private long total;

    PageBudget(long maxBytes) {
        this.maxBytes = maxBytes;
    }

    /** Decoded size of an image: four bytes per pixel. */
    static long estimate(double width, double height) {
        return (long) Math.ceil(width) * (long) Math.ceil(height) * 4;
    }

    void add(int page, long bytes) {
        Long previous = sizes.put(page, bytes);
        total += bytes - (previous == null ? 0 : previous);
    }

    void touch(int page) {
        sizes.get(page); // access order: marks the page as recently used
    }

    void remove(int page) {
        Long removed = sizes.remove(page);
        if (removed != null) {
            total -= removed;
        }
    }

    long total() {
        return total;
    }

    /** Drops pages until the budget fits again and returns them. Protected pages are never dropped. */
    List<Integer> evict(Set<Integer> protectedPages) {
        List<Integer> dropped = new ArrayList<>();
        if (total <= maxBytes) {
            return dropped;
        }
        for (int page : new ArrayList<>(sizes.keySet())) {
            if (total <= maxBytes) {
                break;
            }
            if (!protectedPages.contains(page)) {
                remove(page);
                dropped.add(page);
            }
        }
        return dropped;
    }
}
