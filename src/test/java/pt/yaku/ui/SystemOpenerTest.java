package pt.yaku.ui;

import static org.junit.jupiter.api.Assertions.*;

import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

public class SystemOpenerTest {

    @Test
    public void testWebLinksAreAllowed() {
        SystemOpener.requireWebUrl("https://example.com/post/1");
        SystemOpener.requireWebUrl(" http://example.com ");
    }

    @Test
    public void testOtherSchemesAndOptionsAreRejected() {
        for (String url : new String[] {"file:///etc/passwd", "smb://host/share", "javascript:alert(1)",
                "--help", "/etc/passwd", "", null}) {
            try {
                SystemOpener.requireWebUrl(url);
                fail("should reject " + url);
            } catch (IllegalArgumentException expected) {
                // ok
            }
        }
    }
}
