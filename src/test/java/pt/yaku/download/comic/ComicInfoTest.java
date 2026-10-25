package pt.yaku.download.comic;

import static org.junit.jupiter.api.Assertions.*;

import java.io.StringReader;
import java.util.List;

import javax.xml.XMLConstants;
import javax.xml.transform.stream.StreamSource;
import javax.xml.validation.SchemaFactory;

import org.junit.jupiter.api.Test;

import pt.yaku.booru.Pool;
import pt.yaku.booru.Post;
import pt.yaku.booru.TagCategory;

public class ComicInfoTest {

    private static Post page(long id, String rating, String tags) {
        Post post = new Post();
        post.setId(id);
        post.setWidth(800);
        post.setHeight(1200);
        post.setRating(rating);
        post.setTags(tags);
        post.addTagCategories("some_artist_(artist) conditional_dnp", TagCategory.ARTIST);
        post.addTagCategories("hero_(series) ", TagCategory.CHARACTER);
        post.addTagCategories("big_franchise", TagCategory.COPYRIGHT);
        return post;
    }

    private static Pool pool() {
        return new Pool(7, "Tom_&_Jerry_<1>", "A \"story\" with\u0001 control chars & markup", "series", 2,
                List.of(1L, 2L), "https://e621.test", "2025-03-04T10:15:30.000+01:00",
                "2026-10-20T10:00:00.000+02:00");
    }

    private static List<ComicInfo.Page> pages() {
        return List.of(
                new ComicInfo.Page(page(1, "explicit", "comic english_text monochrome greyscale hero_(series) "
                        + "some_artist_(artist) conditional_dnp big_franchise manga"), 1000),
                new ComicInfo.Page(page(2, "safe", "comic english_text monochrome hero_(series) "
                        + "some_artist_(artist) big_franchise manga"), 2000));
    }

    private static String xml() {
        return ComicInfo.xml(pool(), pages(), "https://e621.test/pools/7", "e621.test", "Kurotsuki 1.0");
    }

    @Test
    public void testOutputValidatesAgainstTheV20Schema() throws Exception {
        var schema = SchemaFactory.newInstance(XMLConstants.W3C_XML_SCHEMA_NS_URI)
                .newSchema(getClass().getResource("/ComicInfo-v2.0.xsd"));
        schema.newValidator().validate(new StreamSource(new StringReader(xml())));
    }

    @Test
    public void testMetadataIsFilledFromThePoolAndItsPosts() {
        String xml = xml();
        assertTrue(xml.contains("<Title>Tom &amp; Jerry &lt;1&gt;</Title>"), xml);
        assertTrue(xml.contains("<Series>Tom &amp; Jerry &lt;1&gt;</Series>"));
        assertTrue(xml.contains("<Summary>A &quot;story&quot; with control chars &amp; markup</Summary>"));
        assertTrue(xml.contains("<Year>2025</Year>") && xml.contains("<Month>3</Month>")
                && xml.contains("<Day>4</Day>"));
        assertTrue(xml.contains("<Writer>some artist</Writer>"), "notice tags are not artists");
        assertTrue(xml.contains("<CoverArtist>some artist</CoverArtist>"));
        assertTrue(xml.contains("<Publisher>e621.test</Publisher>"));
        assertTrue(xml.contains("<Web>https://e621.test/pools/7</Web>"));
        assertTrue(xml.contains("<PageCount>2</PageCount>"));
        assertTrue(xml.contains("<LanguageISO>en</LanguageISO>"));
        assertTrue(xml.contains("<Manga>Yes</Manga>"));
        assertTrue(xml.contains("<Characters>hero (series)</Characters>"));
        assertTrue(xml.contains("<SeriesGroup>big franchise</SeriesGroup>"));
        assertTrue(xml.contains("<MainCharacterOrTeam>hero (series)</MainCharacterOrTeam>"));
        assertTrue(xml.contains("<AgeRating>Adults Only 18+</AgeRating>"), "the strictest page decides");
        assertTrue(xml.contains("<BlackAndWhite>Yes</BlackAndWhite>"));
    }

    @Test
    public void testPagesDescribeEveryImage() {
        String xml = xml();
        assertTrue(xml.contains("<Page Image=\"0\" Type=\"FrontCover\" ImageSize=\"1000\" ImageWidth=\"800\" "
                + "ImageHeight=\"1200\" Key=\"1\" />"), xml);
        assertTrue(xml.contains("<Page Image=\"1\" ImageSize=\"2000\""), xml);
    }

    @Test
    public void testUnknownValuesAreLeftOut() {
        Post bare = new Post();
        bare.setId(5);
        Pool pool = new Pool(1, null, null, "collection", 1, List.of(5L), "https://e621.test", null, null);
        String xml = ComicInfo.xml(pool, List.of(new ComicInfo.Page(bare, 10)), "u", "e621.test", "K");
        assertFalse(xml.contains("<Year>"));
        assertFalse(xml.contains("<Writer>"));
        assertFalse(xml.contains("<AgeRating>"));
        assertFalse(xml.contains("<Manga>"));
        assertFalse(xml.contains("<Summary>"));
        assertTrue(xml.contains("<Title>Pool #1</Title>"));
    }
}
