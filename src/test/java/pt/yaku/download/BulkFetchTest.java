package pt.yaku.download;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.ArrayList;
import java.util.List;

import org.junit.jupiter.api.Test;

import pt.yaku.booru.Post;
import pt.yaku.client.HostThrottle;

public class BulkFetchTest {

    private static Post post(long id) {
        Post post = new Post();
        post.setId(id);
        post.setBooruBaseUrl("https://x");
        return post;
    }

    private static BulkFetch.Pages pages(int pageSize, int totalPosts, List<Integer> requested) {
        return page -> {
            requested.add(page);
            List<Post> out = new ArrayList<>();
            for (int i = page * pageSize; i < Math.min(totalPosts, (page + 1) * pageSize); i++) {
                out.add(post(i));
            }
            return out;
        };
    }

    private static BulkFetch.Result run(BulkFetch.Pages pages, int first, int last, int max,
                                        java.util.function.Predicate<Post> keep) {
        return BulkFetch.fetch(pages, first, last, 10, max, keep, new HostThrottle(0), "x", () -> false, n -> { });
    }

    @Test
    public void testReadsRequestedRange() {
        List<Integer> requested = new ArrayList<>();
        BulkFetch.Result result = run(pages(10, 100, requested), 2, 4, 1000, p -> true);
        assertEquals(List.of(2, 3, 4), requested);
        assertEquals(30, result.posts().size());
        assertEquals(20, result.posts().get(0).getId());
        assertFalse(result.truncated());
        assertNull(result.error());
    }

    @Test
    public void testStopsAtShortPage() {
        List<Integer> requested = new ArrayList<>();
        BulkFetch.Result result = run(pages(10, 25, requested), 0, 9, 1000, p -> true);
        assertEquals(List.of(0, 1, 2), requested);
        assertEquals(25, result.posts().size());
    }

    @Test
    public void testHardLimitTruncates() {
        BulkFetch.Result result = run(pages(10, 100, new ArrayList<>()), 0, 9, 25, p -> true);
        assertEquals(25, result.posts().size());
        assertTrue(result.truncated());
    }

    @Test
    public void testFilterAndDuplicates() {
        BulkFetch.Pages repeating = page -> List.of(post(1), post(1), post(2), post(3), post(4),
                post(5), post(6), post(7), post(8), post(9));
        BulkFetch.Result result = run(repeating, 0, 1, 1000, p -> p.getId() % 2 == 0);
        assertEquals(List.of(2L, 4L, 6L, 8L), result.posts().stream().map(Post::getId).toList());
    }

    @Test
    public void testFailureKeepsWhatWasRead() {
        BulkFetch.Pages failing = page -> {
            if (page == 1) {
                throw new IllegalStateException("HTTP 503");
            }
            return pages(10, 100, new ArrayList<>()).page(page);
        };
        BulkFetch.Result result = run(failing, 0, 5, 1000, p -> true);
        assertEquals(10, result.posts().size());
        assertEquals("HTTP 503", result.error());
    }

    @Test
    public void testCancelledStopsBeforeNextPage() {
        List<Integer> requested = new ArrayList<>();
        BulkFetch.Result result = BulkFetch.fetch(pages(10, 100, requested), 0, 5, 10, 1000, p -> true,
                new HostThrottle(0), "x", () -> requested.size() >= 2, n -> { });
        assertEquals(List.of(0, 1), requested);
        assertEquals(20, result.posts().size());
    }

    @Test
    public void testPageCountIsCapped() {
        List<Integer> requested = new ArrayList<>();
        run(pages(10, 100_000, requested), 0, 10_000, 100_000, p -> true);
        assertEquals(BulkFetch.MAX_PAGES, requested.size());
    }
}
