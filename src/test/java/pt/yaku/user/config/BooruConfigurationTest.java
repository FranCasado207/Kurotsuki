package pt.yaku.user.config;

import static org.junit.jupiter.api.Assertions.*;

import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

public class BooruConfigurationTest {

    private static BooruConfiguration booru(String url, String key) {
        BooruConfiguration booru = new BooruConfiguration();
        booru.setCustomBaseUrl(url);
        booru.setApiKey(key);
        return booru;
    }

    @Test
    public void testRejectsNonHttpOrMalformedUrls() {
        for (String url : new String[] {"file:///etc", "ftp://h/", "not a url", "http://", "gelbooru.com"}) {
            try {
                booru(url, null).validate();
                fail("should reject " + url);
            } catch (IllegalStateException expected) {
                // ok
            }
        }
        booru("https://gelbooru.com", null).validate();
    }

    @Test
    public void testDetectsCredentialsOverPlainHttp() {
        assertTrue(booru("http://booru.example", "key").sendsCredentialsInClear());
        assertFalse(booru("http://127.0.0.1:45869", "key").sendsCredentialsInClear());
        assertFalse(booru("https://booru.example", "key").sendsCredentialsInClear());
        assertFalse(booru("http://booru.example", null).sendsCredentialsInClear());
    }
}
