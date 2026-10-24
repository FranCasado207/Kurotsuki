package pt.yaku.client;

import static org.junit.jupiter.api.Assertions.*;

import java.io.IOException;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Duration;
import java.util.List;

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
            MediaDownloader downloader = new MediaDownloader(new RequestAuth(List::of), Duration.ofMillis(400));
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
}
