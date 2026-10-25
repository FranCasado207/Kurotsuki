package pt.yaku.download;

import static org.junit.jupiter.api.Assertions.*;

import java.io.IOException;
import java.lang.reflect.Proxy;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.concurrent.CompletableFuture;
import java.util.stream.Stream;
import java.util.zip.ZipFile;

import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import pt.yaku.booru.BooruClient;
import pt.yaku.booru.Pool;
import pt.yaku.booru.Post;

public class PoolArchiverTest {

    private Path dir;
    private final List<String> fetched = new ArrayList<>();

    @BeforeEach
    void setUp() throws IOException {
        dir = Files.createTempDirectory("kurotsuki-pool-test");
    }

    @AfterEach
    void tearDown() throws IOException {
        try (Stream<Path> files = Files.walk(dir)) {
            files.sorted(Comparator.reverseOrder()).forEach(p -> p.toFile().delete());
        }
    }

    private static Post post(long id, String image, String url) {
        Post post = new Post();
        post.setId(id);
        post.setImage(image);
        post.setFileUrl(url);
        post.setWidth(10);
        post.setHeight(20);
        post.setRating("safe");
        post.setTags("comic");
        return post;
    }

    private static final byte[] PNG = {(byte) 0x89, 'P', 'N', 'G', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0};

    private PoolArchiver archiver(String format, List<Post> posts) {
        BooruClient client = new BooruClient() {
            @Override
            public List<Post> searchPosts(String tags, int page, int limit) {
                return List.of();
            }

            @Override
            public List<String> suggestTags(String prefix, int limit) {
                return List.of();
            }

            @Override
            public List<Post> poolPosts(Pool pool) {
                return posts;
            }
        };
        return new PoolArchiver(() -> format, pool -> new PoolArchiver.Source(client, "https://e621.test/", "e621.test"),
                url -> {
                    fetched.add(url);
                    try {
                        Path file = Files.createTempFile(dir, "page", ".bin");
                        Files.write(file, PNG);
                        return CompletableFuture.completedFuture(file);
                    } catch (IOException e) {
                        return CompletableFuture.failedFuture(e);
                    }
                });
    }

    private static Pool pool() {
        return new Pool(7, "A/B_Story", "d", "series", 3, List.of(1L, 2L, 3L), "https://e621.test", null, null);
    }

    @Test
    public void testPagesAreArchivedInPoolOrderWithComicInfo() throws Exception {
        List<Post> posts = List.of(post(1, "a.png", "https://s/a"), post(2, "b.jpg", "https://s/b"),
                post(3, "clip.webm", "https://s/c"));
        List<Double> progress = new ArrayList<>();
        Path archive = archiver("cbz", posts).archive(pool(), dir.resolve("out"), progress::add);

        assertEquals("A_B Story.cbz", archive.getFileName().toString(), "the title cannot become a path");
        try (ZipFile zip = new ZipFile(archive.toFile())) {
            List<String> names = zip.stream().map(entry -> entry.getName()).toList();
            // The second page's bytes are PNG data, so detection overrides the .jpg in its name.
            assertEquals(List.of("ComicInfo.xml", "001.png", "002.png"), names, "the video is not a page");
            String xml = new String(zip.getInputStream(zip.getEntry("ComicInfo.xml")).readAllBytes(),
                    StandardCharsets.UTF_8);
            assertTrue(xml.contains("<PageCount>2</PageCount>") && xml.contains("<Title>A/B Story</Title>"), xml);
            assertTrue(xml.contains("<Web>https://e621.test/pools/7</Web>"), xml);
        }
        assertEquals(List.of("https://s/a", "https://s/b"), fetched);
        assertEquals(1.0, progress.get(progress.size() - 1));
    }

    @Test
    public void testExistingArchivesAreNotOverwritten() throws Exception {
        List<Post> posts = List.of(post(1, "a.png", "https://s/a"));
        Path first = archiver("cbz", posts).archive(pool(), dir.resolve("out"), p -> { });
        Path second = archiver("cbz", posts).archive(pool(), dir.resolve("out"), p -> { });
        assertNotEquals(first, second);
        assertTrue(second.getFileName().toString().endsWith("(1).cbz"));
    }

    @Test
    public void testUnknownConfiguredFormatIsReported() {
        IOException error = assertThrows(IOException.class, () ->
                archiver("exe", List.of(post(1, "a.png", "https://s/a"))).archive(pool(), dir, p -> { }));
        assertTrue(error.getMessage().contains("exe"), error.getMessage());
    }

    @Test
    public void testDefaultFormatIsCbzAndPoolWithoutPagesFails() {
        assertThrows(IOException.class, () -> archiver(null, List.of(post(1, "clip.mp4", "https://s/a")))
                .archive(pool(), dir, p -> { }));
    }

    @Test
    public void testFailedPageAbortsWithoutLeavingAnArchive() throws Exception {
        PoolArchiver failing = new PoolArchiver(() -> "cbz", pool -> new PoolArchiver.Source(
                (BooruClient) Proxy.newProxyInstance(getClass().getClassLoader(), new Class<?>[] {BooruClient.class},
                        (proxy, method, args) -> method.getName().equals("poolPosts")
                                ? List.of(post(1, "a.png", "https://s/a")) : null),
                "https://e621.test", "e621.test"),
                url -> CompletableFuture.failedFuture(new IOException("HTTP 404")));
        Path out = dir.resolve("out");
        IOException error = assertThrows(IOException.class, () -> failing.archive(pool(), out, p -> { }));
        assertTrue(error.getMessage().contains("404"), error.getMessage());
        assertFalse(Files.exists(out) && Files.list(out).findAny().isPresent());
    }
}
