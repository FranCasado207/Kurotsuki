package pt.yaku.booru;

import java.util.Collection;
import java.util.List;
import java.util.Map;

public interface BooruClient {

    List<Post> searchPosts(String tags, int page, int limit);

    List<String> suggestTags(String prefix, int limit);

    /** Whether this booru has pools (ordered collections such as comics). */
    default boolean supportsPools() {
        return false;
    }

    /** {@code category} is {@link Pool#SERIES}, {@link Pool#COLLECTION} or null for both; {@code page} starts at 0. */
    default List<Pool> searchPools(String nameContains, String category, PoolOrder order, int page, int limit) {
        return List.of();
    }

    /** False when the booru ignores the category and order arguments of {@link #searchPools}. */
    default boolean supportsPoolFilters() {
        return true;
    }

    /**
     * How many pools one page holds, if the server fixes it. A shorter page then means the last one; with
     * {@code requested} the client's own limit applies.
     */
    default int poolPageSize(int requested) {
        return requested;
    }

    default Pool getPool(long id) {
        return null;
    }

    /** The first post of each pool, keyed by pool id, for showing a cover. Pools without a known cover are left out. */
    default Map<Long, Post> poolCovers(List<Pool> pools) {
        return Map.of();
    }

    /** All posts of the pool that are visible to the account, in the pool's reading order. */
    default List<Post> poolPosts(Pool pool) {
        return List.of();
    }

    default Map<String, TagCategory> tagCategories(Collection<String> names) {
        return Map.of();
    }
}
