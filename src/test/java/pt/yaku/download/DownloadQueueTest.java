package pt.yaku.download;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;

import static org.junit.jupiter.api.Assertions.*;

import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import pt.yaku.booru.Post;

public class DownloadQueueTest {

    private static Post post(String image, String fileUrl) {
        Post post = new Post();
        post.setId(42);
        post.setImage(image);
        post.setFileUrl(fileUrl);
        return post;
    }

    @Test
    public void testTraversalInImageNameIsReducedToLastSegment() {
        assertEquals("passwd", DownloadQueue.safeFileName(post("../../etc/passwd", "https://x/a.png")));
        assertEquals("evil.desktop", DownloadQueue.safeFileName(post("/home/u/.config/autostart/evil.desktop", null)));
        assertEquals("a.png", DownloadQueue.safeFileName(post("..\\..\\a.png", null)));
    }

    @Test
    public void testDotNamesAndBlankFallBackToPostId() {
        assertEquals("post-42", DownloadQueue.safeFileName(post("..", null)));
        assertEquals("post-42", DownloadQueue.safeFileName(post("  ", null)));
        assertEquals("post-42", DownloadQueue.safeFileName(post(null, "https://x/")));
    }

    @Test
    public void testFileUrlFallbackDropsQueryString() {
        assertEquals("file.jpg", DownloadQueue.safeFileName(post(null, "https://x/data/file.jpg?token=abc")));
    }

    @Test
    public void testReservedCharactersAreReplaced() {
        assertEquals("a_b_c.png", DownloadQueue.safeFileName(post("a:b*c.png", null)));
    }

    @Test
    public void testUniqueTargetStaysInsideFolderAndDoesNotOverwrite() throws IOException {
        Path dir = Files.createTempDirectory("kurotsuki-dl");
        try {
            Path first = DownloadQueue.uniqueTarget(dir, "a.png");
            assertEquals(dir.toRealPath(), first.getParent().toRealPath());
            Files.writeString(first, "x");
            Path second = DownloadQueue.uniqueTarget(dir, "a.png");
            assertEquals("a (1).png", second.getFileName().toString());
            try {
                DownloadQueue.uniqueTarget(dir, "../escape.png");
                fail("expected IOException");
            } catch (IOException expected) {
                // ok
            }
        } finally {
            try (var files = Files.list(dir)) {
                for (Path file : files.toList()) {
                    Files.deleteIfExists(file);
                }
            }
            Files.deleteIfExists(dir);
        }
    }
}
