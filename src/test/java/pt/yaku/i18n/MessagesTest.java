package pt.yaku.i18n;

import static org.junit.jupiter.api.Assertions.*;

import java.io.IOException;
import java.io.InputStream;
import java.util.HashSet;
import java.util.Properties;
import java.util.Set;

import org.junit.jupiter.api.Test;

public class MessagesTest {

    private static Properties load(String name) throws IOException {
        Properties properties = new Properties();
        try (InputStream in = MessagesTest.class.getResourceAsStream(name)) {
            assertNotNull(in, name);
            properties.load(in);
        }
        return properties;
    }

    @Test
    public void testEveryLanguageHasTheSameKeys() throws IOException {
        Set<String> english = new HashSet<>(load("/messages.properties").stringPropertyNames());
        Set<String> portuguese = new HashSet<>(load("/messages_pt.properties").stringPropertyNames());
        Set<String> missingInPortuguese = new HashSet<>(english);
        missingInPortuguese.removeAll(portuguese);
        Set<String> missingInEnglish = new HashSet<>(portuguese);
        missingInEnglish.removeAll(english);
        assertTrue(missingInPortuguese.isEmpty(), "missing in messages_pt: " + missingInPortuguese);
        assertTrue(missingInEnglish.isEmpty(), "missing in messages: " + missingInEnglish);
    }
}
