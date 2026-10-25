package pt.yaku.client;

import static org.junit.jupiter.api.Assertions.*;

import java.io.IOException;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Duration;
import java.time.Instant;
import java.util.List;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.concurrent.atomic.AtomicInteger;

import org.junit.jupiter.api.Test;

import com.sun.net.httpserver.HttpServer;

public class MediaDownloaderTest {

    private static HttpServer server(boolean stall) throws IOException {
        HttpServer server = HttpServer.create(new InetSocketAddress("127.0.0.1", 0), 0);
        server.createContext("/", exchange -> {
            if (stall) {
                exchange.sendResponseHeaders(200, 1000);
                OutputStream out = exchange.getResponseBody();
                out.write(new byte[10]);
                out.flush();
                try {
                    Thread.sleep(2_000);
                } catch (InterruptedException e) {
                    Thread.currentThread().interrupt();
                }
            } else {
                byte[] body = "hello".getBytes();
                exchange.sendResponseHeaders(200, body.length);
                try (OutputStream out = exchange.getResponseBody()) {
                    out.write(body);
                }
            }
        });
        server.start();
        return server;
    }

    @Test
    public void testDownloadsAndLeavesNoPartFile() throws Exception {
        HttpServer server = server(false);
        Path dir = Files.createTempDirectory("kurotsuki-http");
        try {
            MediaDownloader downloader = new MediaDownloader(new RequestAuth(List::of));
            Path target = dir.resolve("a.bin");
            downloader.downloadBlocking("http://127.0.0.1:" + server.getAddress().getPort() + "/a", target, p -> { });
            assertEquals("hello", Files.readString(target));
            assertFalse(Files.exists(dir.resolve("a.bin.part")));
        } finally {
            server.stop(0);
            try (var files = Files.list(dir)) {
                files.forEach(p -> p.toFile().delete());
            }
            Files.deleteIfExists(dir);
        }
    }

    @Test
    public void testStalledBodyIsAborted() throws Exception {
        HttpServer server = server(true);
        Path dir = Files.createTempDirectory("kurotsuki-http");
        try {
            MediaDownloader downloader = new MediaDownloader(new RequestAuth(List::of), Duration.ofMillis(400),
                    Duration.ofMillis(10), 1);
            Path target = dir.resolve("a.bin");
            long start = System.nanoTime();
            assertThrows(IOException.class, () -> downloader.downloadBlocking(
                    "http://127.0.0.1:" + server.getAddress().getPort() + "/a", target, p -> { }));
            assertTrue(Duration.ofNanos(System.nanoTime() - start).toSeconds() < 5, "should abort quickly");
            assertFalse(Files.exists(target));
            assertFalse(Files.exists(dir.resolve("a.bin.part")));
        } finally {
            server.stop(0);
            try (var files = Files.list(dir)) {
                files.forEach(p -> p.toFile().delete());
            }
            Files.deleteIfExists(dir);
        }
    }

    private static void cleanUp(HttpServer server, Path dir) throws IOException {
        server.stop(0);
        try (var files = Files.list(dir)) {
            files.forEach(p -> p.toFile().delete());
        }
        Files.deleteIfExists(dir);
    }

    private static MediaDownloader fast(int attempts) {
        return new MediaDownloader(new RequestAuth(List::of), Duration.ofSeconds(5), Duration.ofMillis(10), attempts);
    }

    @Test
    public void testRetriesAfterRateLimitAndServerError() throws Exception {
        AtomicInteger calls = new AtomicInteger();
        HttpServer server = HttpServer.create(new InetSocketAddress("127.0.0.1", 0), 0);
        server.createContext("/", exchange -> {
            int call = calls.incrementAndGet();
            if (call <= 2) {
                exchange.getResponseHeaders().add("Retry-After", "0");
                exchange.sendResponseHeaders(call == 1 ? 429 : 503, -1);
                exchange.close();
                return;
            }
            byte[] body = "ok".getBytes();
            exchange.sendResponseHeaders(200, body.length);
            try (OutputStream out = exchange.getResponseBody()) {
                out.write(body);
            }
        });
        server.start();
        Path dir = Files.createTempDirectory("kurotsuki-http");
        try {
            Path target = dir.resolve("a.bin");
            fast(4).downloadBlocking("http://127.0.0.1:" + server.getAddress().getPort() + "/a", target, p -> { });
            assertEquals("ok", Files.readString(target));
            assertEquals(3, calls.get());
        } finally {
            cleanUp(server, dir);
        }
    }

    @Test
    public void testGivesUpAfterMaxAttemptsAndDoesNotRetryClientErrors() throws Exception {
        AtomicInteger calls = new AtomicInteger();
        HttpServer server = HttpServer.create(new InetSocketAddress("127.0.0.1", 0), 0);
        server.createContext("/missing", exchange -> {
            calls.incrementAndGet();
            exchange.sendResponseHeaders(404, -1);
            exchange.close();
        });
        server.createContext("/busy", exchange -> {
            calls.incrementAndGet();
            exchange.sendResponseHeaders(503, -1);
            exchange.close();
        });
        server.start();
        Path dir = Files.createTempDirectory("kurotsuki-http");
        try {
            String base = "http://127.0.0.1:" + server.getAddress().getPort();
            IOException notFound = assertThrows(IOException.class,
                    () -> fast(4).downloadBlocking(base + "/missing", dir.resolve("a"), p -> { }));
            assertEquals("HTTP 404", notFound.getMessage());
            assertEquals(1, calls.get());
            calls.set(0);
            IOException busy = assertThrows(IOException.class,
                    () -> fast(3).downloadBlocking(base + "/busy", dir.resolve("b"), p -> { }));
            assertEquals("HTTP 503", busy.getMessage());
            assertEquals(3, calls.get());
        } finally {
            cleanUp(server, dir);
        }
    }

    @Test
    public void testResumesInterruptedTransferWithRange() throws Exception {
        byte[] content = "0123456789abcdefghij".getBytes();
        AtomicInteger calls = new AtomicInteger();
        List<String> ranges = new CopyOnWriteArrayList<>();
        HttpServer server = HttpServer.create(new InetSocketAddress("127.0.0.1", 0), 0);
        server.createContext("/", exchange -> {
            int call = calls.incrementAndGet();
            exchange.getResponseHeaders().add("ETag", "\"v1\"");
            String range = exchange.getRequestHeaders().getFirst("Range");
            ranges.add(String.valueOf(range));
            if (range == null) {
                exchange.sendResponseHeaders(200, content.length);
                exchange.getResponseBody().write(content, 0, 10);
                exchange.getResponseBody().flush();
                exchange.close();
                return;
            }
            int from = Integer.parseInt(range.substring(6, range.indexOf('-')));
            exchange.getResponseHeaders().add("Content-Range",
                    "bytes " + from + "-" + (content.length - 1) + "/" + content.length);
            exchange.sendResponseHeaders(206, content.length - from);
            try (OutputStream out = exchange.getResponseBody()) {
                out.write(content, from, content.length - from);
            }
        });
        server.start();
        Path dir = Files.createTempDirectory("kurotsuki-http");
        try {
            Path target = dir.resolve("a.bin");
            fast(3).downloadBlocking("http://127.0.0.1:" + server.getAddress().getPort() + "/a", target, p -> { });
            assertArrayEquals(content, Files.readAllBytes(target));
            assertEquals(List.of("null", "bytes=10-"), ranges);
            assertFalse(Files.exists(dir.resolve("a.bin.part")));
            assertFalse(Files.exists(dir.resolve("a.bin.part.meta")));
        } finally {
            cleanUp(server, dir);
        }
    }

    @Test
    public void testRetryAfterParsing() {
        Instant now = Instant.parse("2026-01-01T00:00:00Z");
        assertEquals(Duration.ofSeconds(7), MediaDownloader.parseRetryAfter("7", now));
        assertEquals(Duration.ofSeconds(30),
                MediaDownloader.parseRetryAfter("Thu, 01 Jan 2026 00:00:30 GMT", now));
        assertEquals(Duration.ZERO, MediaDownloader.parseRetryAfter("Wed, 31 Dec 2025 23:00:00 GMT", now));
        assertNull(MediaDownloader.parseRetryAfter("soon", now));
        assertNull(MediaDownloader.parseRetryAfter(null, now));
    }

    @Test
    public void testBackoffDoublesAndIsCapped() {
        MediaDownloader downloader = new MediaDownloader(new RequestAuth(List::of), Duration.ofSeconds(5),
                Duration.ofSeconds(1), 4);
        assertEquals(Duration.ofSeconds(1), downloader.backoff(1));
        assertEquals(Duration.ofSeconds(2), downloader.backoff(2));
        assertEquals(Duration.ofSeconds(4), downloader.backoff(3));
        assertEquals(Duration.ofSeconds(60), downloader.backoff(20));
    }
}
