package pt.yaku.ui;

import static org.junit.jupiter.api.Assertions.*;

import java.util.List;
import java.util.Set;

import org.junit.jupiter.api.Test;

public class SpreadLayoutTest {

    private static List<List<Integer>> plain(List<int[]> groups) {
        return groups.stream().map(group -> java.util.Arrays.stream(group).boxed().toList()).toList();
    }

    @Test
    public void testCoverStandsAloneAndTheRestArePaired() {
        assertEquals(List.of(List.of(0), List.of(1, 2), List.of(3, 4), List.of(5)),
                plain(SpreadLayout.spreads(6, page -> false, true)));
    }

    @Test
    public void testWithoutACoverPairsStartAtTheFirstPage() {
        assertEquals(List.of(List.of(0, 1), List.of(2, 3)), plain(SpreadLayout.spreads(4, page -> false, false)));
    }

    @Test
    public void testWidePagesAreShownAloneAndDoNotPairWithNeighbours() {
        Set<Integer> wide = Set.of(2);
        assertEquals(List.of(List.of(0), List.of(1), List.of(2), List.of(3, 4)),
                plain(SpreadLayout.spreads(5, wide::contains, true)));
    }

    @Test
    public void testSinglesAndLookup() {
        List<int[]> singles = SpreadLayout.singles(3);
        assertEquals(3, singles.size());
        List<int[]> spreads = SpreadLayout.spreads(6, page -> false, true);
        assertEquals(1, SpreadLayout.groupOf(spreads, 1));
        assertEquals(1, SpreadLayout.groupOf(spreads, 2));
        assertEquals(3, SpreadLayout.groupOf(spreads, 5));
    }

    @Test
    public void testEmptyComicHasNoGroups() {
        assertTrue(SpreadLayout.spreads(0, page -> false, true).isEmpty());
    }
}
