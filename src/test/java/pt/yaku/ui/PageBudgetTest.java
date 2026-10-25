package pt.yaku.ui;

import static org.junit.jupiter.api.Assertions.*;

import java.util.List;
import java.util.Set;

import org.junit.jupiter.api.Test;

public class PageBudgetTest {

    @Test
    public void testNothingIsDroppedWhileUnderBudget() {
        PageBudget budget = new PageBudget(100);
        budget.add(0, 40);
        budget.add(1, 40);
        assertEquals(List.of(), budget.evict(Set.of()));
        assertEquals(80, budget.total());
    }

    @Test
    public void testLeastRecentlyUsedPagesGoFirst() {
        PageBudget budget = new PageBudget(100);
        budget.add(0, 40);
        budget.add(1, 40);
        budget.add(2, 40);
        budget.touch(0); // page 1 is now the oldest
        assertEquals(List.of(1), budget.evict(Set.of()));
        assertEquals(80, budget.total());
    }

    @Test
    public void testProtectedPagesSurviveEvenOverBudget() {
        PageBudget budget = new PageBudget(50);
        budget.add(0, 40);
        budget.add(1, 40);
        budget.add(2, 40);
        assertEquals(List.of(2), budget.evict(Set.of(0, 1)).stream().filter(p -> p == 2).toList());
        assertEquals(80, budget.total(), "protected pages stay although they exceed the budget");
    }

    @Test
    public void testReplacingAPageAdjustsTheTotal() {
        PageBudget budget = new PageBudget(1000);
        budget.add(3, 100);
        budget.add(3, 60);
        assertEquals(60, budget.total());
        budget.remove(3);
        assertEquals(0, budget.total());
    }

    @Test
    public void testEstimateIsFourBytesPerPixel() {
        assertEquals(4L * 1000 * 2000, PageBudget.estimate(1000, 2000));
    }
}
