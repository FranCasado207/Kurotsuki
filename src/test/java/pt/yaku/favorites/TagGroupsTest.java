package pt.yaku.favorites;

import static org.junit.jupiter.api.Assertions.assertEquals;

import java.util.List;

import org.junit.jupiter.api.Test;

public class TagGroupsTest {

    @Test
    public void testFirstMatchingGroupWins() {
        List<TagGroup> groups = List.of(
                new TagGroup("Cats", List.of("cat", "kitten")),
                new TagGroup("Dogs", List.of("dog")));
        assertEquals("Cats", TagGroups.firstGroup(groups, "dog cat solo"));
        assertEquals("Dogs", TagGroups.firstGroup(groups, "solo   dog"));
        assertEquals("", TagGroups.firstGroup(groups, "bird"));
        assertEquals("", TagGroups.firstGroup(groups, null));
        assertEquals("", TagGroups.firstGroup(groups, "  "));
    }
}
