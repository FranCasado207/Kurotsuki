package pt.yaku.download;

import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Set;
import java.util.function.BooleanSupplier;
import java.util.function.IntConsumer;
import java.util.function.Predicate;

import pt.yaku.booru.Post;
import pt.yaku.client.HostThrottle;

/** Collects the posts of several result pages for a bulk download, with a hard cap and a pause between pages. */
public final class BulkFetch {

    /** Never collect more than this many posts in one go. */
    public static final int MAX_POSTS = 1000;
    public static final int MAX_PAGES = 50;
    /** Milliseconds between two search requests to the same booru. */
    public static final long PAGE_SPACING_MILLIS = 1000;

    @FunctionalInterface
    public interface Pages {
        /** Zero-based page of results. */
        List<Post> page(int page) throws Exception;
    }

    /** {@code truncated}: stopped because of the post cap; {@code error}: the page that failed, if any. */
    public record Result(List<Post> posts, boolean truncated, String error, int pagesRead) {
    }

    private BulkFetch() {
    }

    /**
     * Reads pages {@code firstPage..lastPage} (zero-based, inclusive). Stops early on a short page (the end of the
     * results), a failure (what was read so far is kept), cancellation or the cap.
     */
    public static Result fetch(Pages source, int firstPage, int lastPage, int pageSize, int maxPosts,
                               Predicate<Post> keep, HostThrottle throttle, String host,
                               BooleanSupplier cancelled, IntConsumer onPage) {
        int limit = Math.min(maxPosts, MAX_POSTS);
        int last = Math.min(lastPage, firstPage + MAX_PAGES - 1);
        List<Post> posts = new ArrayList<>();
        Set<String> seen = new HashSet<>();
        int read = 0;
        for (int page = firstPage; page <= last; page++) {
            if (cancelled.getAsBoolean()) {
                break;
            }
            List<Post> received;
            try {
                throttle.acquire(host);
                received = source.page(page);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                return new Result(posts, false, null, read);
            } catch (Exception e) {
                return new Result(posts, false, e.getMessage() != null ? e.getMessage() : e.toString(), read);
            }
            read++;
            onPage.accept(read);
            for (Post post : received) {
                if (!keep.test(post) || !seen.add(post.key())) {
                    continue;
                }
                if (posts.size() >= limit) {
                    return new Result(posts, true, null, read);
                }
                posts.add(post);
            }
            if (received.size() < pageSize) {
                break;
            }
        }
        return new Result(posts, false, null, read);
    }
}
