package pt.yaku.ui;

import java.util.ArrayList;
import java.util.List;
import java.util.function.IntPredicate;

/** Groups the pages of a comic into what the reader shows at once: single pages or two-page spreads. */
final class SpreadLayout {

    private SpreadLayout() {
    }

    static List<int[]> singles(int count) {
        List<int[]> groups = new ArrayList<>();
        for (int i = 0; i < count; i++) {
            groups.add(new int[] {i});
        }
        return groups;
    }

    /**
     * Pairs consecutive pages like an open book. The cover stands alone (when {@code coverAlone}), and a wide
     * page, which already is a spread, is shown alone and does not take its neighbour with it.
     */
    static List<int[]> spreads(int count, IntPredicate isWide, boolean coverAlone) {
        List<int[]> groups = new ArrayList<>();
        int i = 0;
        while (i < count) {
            boolean alone = (i == 0 && coverAlone) || isWide.test(i) || i + 1 >= count || isWide.test(i + 1);
            if (alone) {
                groups.add(new int[] {i});
                i++;
            } else {
                groups.add(new int[] {i, i + 1});
                i += 2;
            }
        }
        return groups;
    }

    /** Index of the group that shows {@code page}. */
    static int groupOf(List<int[]> groups, int page) {
        for (int g = 0; g < groups.size(); g++) {
            for (int p : groups.get(g)) {
                if (p == page) {
                    return g;
                }
            }
        }
        return Math.max(0, groups.size() - 1);
    }
}
