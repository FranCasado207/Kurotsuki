package pt.yaku.download;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;
import pt.yaku.booru.Post;
import pt.yaku.booru.TagCategory;

public class FileNameTemplateTest {

    private static final String MD5 = "0123456789abcdef0123456789abcdef";

    private static Post post() {
        Post post = new Post();
        post.setId(42);
        post.setImage(MD5 + ".png");
        post.setRating("s");
        post.setScore(7);
        post.setTags("cat bob_ross");
        post.setBooruBaseUrl("https://e621.net");
        post.addTagCategories("bob_ross", TagCategory.ARTIST);
        return post;
    }

    @Test
    public void testExampleTemplate() {
        FileNameTemplate template = new FileNameTemplate("%booru%/%artist%/%id%_%md5%.%ext%", null);
        assertEquals("e621.net/bob_ross/42_" + MD5 + ".png", template.render(post()));
    }

    @Test
    public void testDefaultKeepsOriginalName() {
        assertEquals(MD5 + ".png", new FileNameTemplate(null, null).render(post()));
        assertEquals(MD5 + ".png", new FileNameTemplate("  ", null).render(post()));
    }

    @Test
    public void testGroupFolderAndEmptyGroupDropsSegment() {
        Post post = post();
        assertEquals("Cats/42.png", new FileNameTemplate("%group%/%id%.%ext%", p -> "Cats").render(post));
        assertEquals("42.png", new FileNameTemplate("%group%/%id%.%ext%", p -> "").render(post));
    }

    @Test
    public void testValuesCannotAddFoldersOrEscape() {
        Post post = post();
        post.setTagCategories(new java.util.LinkedHashMap<>());
        post.addTagCategories("../../evil", TagCategory.ARTIST);
        String path = new FileNameTemplate("%artist%/%id%.%ext%", null).render(post);
        assertFalse(java.util.Arrays.asList(path.split("/")).contains(".."), path);
        assertEquals(2, path.split("/").length, path);
        String literal = new FileNameTemplate("../../x/%id%", null).render(post());
        assertFalse(java.util.Arrays.asList(literal.split("/")).contains(".."), literal);
        assertTrue(literal.endsWith("x/42.png"), literal);
    }

    @Test
    public void testExtensionIsAddedWhenTemplateHasNone() {
        assertEquals("post_42.png", new FileNameTemplate("post_%id%", null).render(post()));
    }

    @Test
    public void testMd5FallsBackToIdWhenNameIsNotAHash() {
        Post post = post();
        post.setImage("cover.jpg");
        assertEquals("42.jpg", new FileNameTemplate("%md5%.%ext%", null).render(post));
    }

    @Test
    public void testUnknownTokenIsKeptAndSanitised() {
        assertEquals("%nope%_42.png", new FileNameTemplate("%nope%_%id%.%ext%", null).render(post()));
    }
}
