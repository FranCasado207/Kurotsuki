package pt.yaku.user.config;

import java.util.Collection;
import java.util.Locale;

import pt.yaku.booru.Post;

public final class TagBlacklist {

    private TagBlacklist() {
    }

    public static String normalize(String tag) {
        if (tag == null) {
            return "";
        }
        return tag.trim().toLowerCase(Locale.ROOT).replace(' ', '_');
    }

    public static boolean isBlocked(Post post, Collection<String> blacklist) {
        if (blacklist == null || blacklist.isEmpty() || post.getTags() == null) {
            return false;
        }
        for (String tag : post.getTags().trim().split("\\s+")) {
            if (blacklist.contains(normalize(tag))) {
                return true;
            }
        }
        return false;
    }
}
