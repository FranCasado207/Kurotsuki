package pt.yaku.client;

import static org.junit.jupiter.api.Assertions.*;

import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

public class RequestAuthTest {

    @Test
    public void testOnlyUrlsUnderTheBooruBaseReceiveCredentials() {
        assertTrue(RequestAuth.isUnder("http://127.0.0.1:45869/get_files/file?file_id=1", "http://127.0.0.1:45869"));
        assertTrue(RequestAuth.isUnder("http://127.0.0.1:45869/x", "http://127.0.0.1:45869/"));
        assertFalse(RequestAuth.isUnder("http://127.0.0.1:458699/x", "http://127.0.0.1:45869"));
        assertFalse(RequestAuth.isUnder("http://evil.test/x", "http://127.0.0.1:45869"));
        assertFalse(RequestAuth.isUnder("http://h/x", null));
    }
}
