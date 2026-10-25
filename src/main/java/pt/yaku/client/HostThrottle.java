package pt.yaku.client;

import java.util.HashMap;
import java.util.Map;
import java.util.concurrent.TimeUnit;
import java.util.function.LongSupplier;

/** Spaces out requests to the same host so bulk work does not hammer a booru and trigger its rate limit. */
public final class HostThrottle {

    private final long intervalNanos;
    private final LongSupplier clock;
    private final Map<String, Long> nextFree = new HashMap<>();

    public HostThrottle(long intervalMillis) {
        this(intervalMillis, System::nanoTime);
    }

    HostThrottle(long intervalMillis, LongSupplier clock) {
        this.intervalNanos = TimeUnit.MILLISECONDS.toNanos(Math.max(0, intervalMillis));
        this.clock = clock;
    }

    /** Books the next request slot for the host and returns how long the caller must wait before using it. */
    synchronized long reserve(String host) {
        long now = clock.getAsLong();
        long start = Math.max(now, nextFree.getOrDefault(host, now));
        nextFree.put(host, start + intervalNanos);
        return start - now;
    }

    public void acquire(String host) throws InterruptedException {
        long wait = reserve(host == null ? "" : host);
        if (wait > 0) {
            TimeUnit.NANOSECONDS.sleep(wait);
        }
    }
}
