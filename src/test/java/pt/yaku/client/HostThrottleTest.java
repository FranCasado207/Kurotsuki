package pt.yaku.client;

import static org.junit.jupiter.api.Assertions.assertEquals;

import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicLong;

import org.junit.jupiter.api.Test;

public class HostThrottleTest {

    @Test
    public void testSpacesRequestsPerHost() {
        AtomicLong now = new AtomicLong();
        HostThrottle throttle = new HostThrottle(100, now::get);
        assertEquals(0, throttle.reserve("a"));
        assertEquals(TimeUnit.MILLISECONDS.toNanos(100), throttle.reserve("a"));
        assertEquals(TimeUnit.MILLISECONDS.toNanos(200), throttle.reserve("a"));
        assertEquals(0, throttle.reserve("b"));
        now.set(TimeUnit.MILLISECONDS.toNanos(1000));
        assertEquals(0, throttle.reserve("a"));
    }
}
