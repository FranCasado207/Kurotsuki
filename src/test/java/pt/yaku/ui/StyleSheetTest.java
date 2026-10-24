package pt.yaku.ui;

import static org.junit.jupiter.api.Assertions.*;

import java.io.IOException;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.HashSet;
import java.util.Set;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

import org.junit.jupiter.api.Test;

import javafx.css.CssParser;

public class StyleSheetTest {

    private static final String SHEET = "/style.css";

    @Test
    public void testStylesheetParsesWithoutErrors() throws IOException {
        URL url = getClass().getResource(SHEET);
        assertNotNull(url);
        CssParser parser = new CssParser();
        parser.parse(url);
        assertTrue(parser.errorsProperty().isEmpty(), "CSS errors: " + parser.errorsProperty());
    }

    @Test
    public void testEveryLookedUpColourIsDefined() throws IOException {
        String css = new String(getClass().getResourceAsStream(SHEET).readAllBytes(), StandardCharsets.UTF_8);
        Set<String> defined = new HashSet<>();
        Matcher definitions = Pattern.compile("(-kt-[a-z0-9-]+)\\s*:").matcher(css);
        while (definitions.find()) {
            defined.add(definitions.group(1));
        }
        Matcher uses = Pattern.compile(":\\s*(-kt-[a-z0-9-]+)\\s*[;,)]").matcher(css);
        while (uses.find()) {
            assertTrue(defined.contains(uses.group(1)), "undefined lookup " + uses.group(1));
        }
    }
}
