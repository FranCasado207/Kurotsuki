package pt.yaku.download.comic;

import java.time.OffsetDateTime;
import java.time.format.DateTimeParseException;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.stream.Collectors;

import pt.yaku.booru.Pool;
import pt.yaku.booru.Post;
import pt.yaku.booru.TagCategory;

/**
 * Builds a ComicInfo.xml (Anansi Project schema v2.0) for a pool. Elements follow the order the schema's
 * {@code xs:sequence} demands, and anything we have no data for is left out rather than guessed.
 */
public final class ComicInfo {

    public static final String FILE_NAME = "ComicInfo.xml";

    /** One page of the archive: the post it came from and the size of the stored file. */
    public record Page(Post post, long size) {
    }

    private static final int MAX_GENRES = 30;
    /** Tags e621 files under "artist" that are notices, not people. */
    private static final Set<String> NOT_ARTISTS = Set.of("conditional_dnp", "sound_warning", "unknown_artist",
            "anonymous_artist", "avoid_posting", "epilepsy_warning", "unknown_artist_signature",
            "third-party_edit", "background_art", "sketch_page");
    private static final Map<String, String> LANGUAGES = Map.ofEntries(
            Map.entry("english_text", "en"), Map.entry("japanese_text", "ja"), Map.entry("spanish_text", "es"),
            Map.entry("french_text", "fr"), Map.entry("german_text", "de"), Map.entry("portuguese_text", "pt"),
            Map.entry("italian_text", "it"), Map.entry("russian_text", "ru"), Map.entry("chinese_text", "zh"),
            Map.entry("korean_text", "ko"));

    private ComicInfo() {
    }

    /**
     * @param webUrl     the pool's page on the booru
     * @param publisher  the booru's host name
     * @param scanner    free text for ScanInformation, normally the application name and version
     */
    public static String xml(Pool pool, List<Page> pages, String webUrl, String publisher, String scanner) {
        List<Post> posts = pages.stream().map(Page::post).toList();
        Map<String, Integer> artists = count(posts, tag -> category(posts, tag) == TagCategory.ARTIST
                && !NOT_ARTISTS.contains(tag));
        Map<String, Integer> characters = count(posts, tag -> category(posts, tag) == TagCategory.CHARACTER);
        Map<String, Integer> copyrights = count(posts, tag -> category(posts, tag) == TagCategory.COPYRIGHT);
        Map<String, Integer> general = count(posts, tag -> category(posts, tag) == TagCategory.GENERAL);
        List<String> firstPageArtists = posts.isEmpty() ? List.of() : tags(posts.get(0)).stream()
                .filter(tag -> posts.get(0).categoryOf(tag) == TagCategory.ARTIST && !NOT_ARTISTS.contains(tag))
                .toList();

        Xml xml = new Xml();
        xml.text("Title", pool.displayName());
        xml.text("Series", pool.displayName());
        xml.text("Summary", pool.description());
        xml.text("Notes", "Downloaded from " + publisher + " (pool #" + pool.id() + ", " + pool.category()
                + ") with " + scanner);
        OffsetDateTime created = date(pool.createdAt() != null ? pool.createdAt() : pool.updatedAt());
        if (created != null) {
            xml.number("Year", created.getYear());
            xml.number("Month", created.getMonthValue());
            xml.number("Day", created.getDayOfMonth());
        }
        String artistNames = join(artists.keySet().stream().map(ComicInfo::person).toList());
        xml.text("Writer", artistNames);
        xml.text("Penciller", artistNames);
        xml.text("CoverArtist", join(firstPageArtists.stream().map(ComicInfo::person).toList()));
        xml.text("Publisher", publisher);
        xml.text("Genre", join(top(general, MAX_GENRES).stream().map(ComicInfo::words).toList()));
        xml.text("Web", webUrl);
        xml.number("PageCount", pages.size());
        xml.text("LanguageISO", language(posts));
        xml.text("Format", "Web");
        xml.text("BlackAndWhite", blackAndWhite(posts));
        xml.text("Manga", share(posts, "manga") >= 0.5 ? "Yes" : null);
        xml.text("Characters", join(characters.keySet().stream().map(ComicInfo::words).toList()));
        xml.text("ScanInformation", scanner);
        xml.text("SeriesGroup", join(copyrights.keySet().stream().map(ComicInfo::words).toList()));
        xml.text("AgeRating", ageRating(posts));
        xml.raw(pagesElement(pages));
        xml.text("MainCharacterOrTeam", top(characters, 1).stream().findFirst().map(ComicInfo::words).orElse(null));
        return xml.build();
    }

    // ---- derived values -------------------------------------------------------------------------------------

    private static String pagesElement(List<Page> pages) {
        if (pages.isEmpty()) {
            return "";
        }
        StringBuilder out = new StringBuilder("  <Pages>\n");
        for (int i = 0; i < pages.size(); i++) {
            Post post = pages.get(i).post();
            out.append("    <Page Image=\"").append(i).append('"');
            if (i == 0) {
                out.append(" Type=\"FrontCover\"");
            }
            out.append(" ImageSize=\"").append(pages.get(i).size()).append('"');
            if (post.getWidth() > 0 && post.getHeight() > 0) {
                out.append(" ImageWidth=\"").append(post.getWidth()).append('"')
                        .append(" ImageHeight=\"").append(post.getHeight()).append('"');
            }
            out.append(" Key=\"").append(post.getId()).append("\" />\n");
        }
        return out.append("  </Pages>\n").toString();
    }

    private static String ageRating(List<Post> posts) {
        int worst = -1;
        for (Post post : posts) {
            String rating = post.getRating() == null ? "" : post.getRating().toLowerCase(Locale.ROOT);
            worst = Math.max(worst, rating.startsWith("e") ? 2 : rating.startsWith("q") ? 1
                    : rating.startsWith("s") || rating.startsWith("g") ? 0 : -1);
        }
        return switch (worst) {
            case 0 -> "Everyone";
            case 1 -> "Mature 17+";
            case 2 -> "Adults Only 18+";
            default -> null;
        };
    }

    private static String blackAndWhite(List<Post> posts) {
        double share = Math.max(share(posts, "monochrome"), share(posts, "greyscale"));
        if (posts.isEmpty()) {
            return null;
        }
        return share >= 1.0 ? "Yes" : share == 0.0 ? "No" : "Unknown";
    }

    private static String language(List<Post> posts) {
        Map<String, Integer> counts = new LinkedHashMap<>();
        for (Post post : posts) {
            for (String tag : tags(post)) {
                String iso = LANGUAGES.get(tag);
                if (iso != null) {
                    counts.merge(iso, 1, Integer::sum);
                }
            }
        }
        return top(counts, 1).stream().findFirst().orElse(null);
    }

    private static double share(List<Post> posts, String tag) {
        if (posts.isEmpty()) {
            return 0;
        }
        return (double) posts.stream().filter(post -> tags(post).contains(tag)).count() / posts.size();
    }

    private static List<String> tags(Post post) {
        String raw = post.getTags();
        return raw == null || raw.isBlank() ? List.of() : List.of(raw.trim().split("\\s+"));
    }

    private static TagCategory category(List<Post> posts, String tag) {
        for (Post post : posts) {
            if (post.getTagCategories() != null && post.getTagCategories().containsKey(tag)) {
                return post.categoryOf(tag);
            }
        }
        return TagCategory.GENERAL;
    }

    /** Tag to the number of pages carrying it, most frequent first. */
    private static Map<String, Integer> count(List<Post> posts, java.util.function.Predicate<String> keep) {
        Map<String, Integer> counts = new LinkedHashMap<>();
        for (Post post : posts) {
            for (String tag : new java.util.LinkedHashSet<>(tags(post))) {
                if (keep.test(tag)) {
                    counts.merge(tag, 1, Integer::sum);
                }
            }
        }
        return counts.entrySet().stream()
                .sorted(Map.Entry.<String, Integer>comparingByValue().reversed())
                .collect(Collectors.toMap(Map.Entry::getKey, Map.Entry::getValue, (a, b) -> a, LinkedHashMap::new));
    }

    private static List<String> top(Map<String, Integer> counts, int limit) {
        return counts.entrySet().stream()
                .sorted(Map.Entry.<String, Integer>comparingByValue().reversed())
                .limit(limit).map(Map.Entry::getKey).toList();
    }

    private static OffsetDateTime date(String value) {
        if (value == null) {
            return null;
        }
        try {
            return OffsetDateTime.parse(value);
        } catch (DateTimeParseException e) {
            return null;
        }
    }

    /** "some_artist_(artist)" becomes "some artist". */
    private static String person(String tag) {
        return words(tag.replaceFirst("_\\(artist\\)$", ""));
    }

    private static String words(String tag) {
        return tag.replace('_', ' ');
    }

    private static String join(List<String> values) {
        return values.isEmpty() ? null : String.join(", ", values);
    }

    // ---- xml output -----------------------------------------------------------------------------------------

    private static final class Xml {

        private final StringBuilder out = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
                + "<ComicInfo xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" "
                + "xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">\n");

        void text(String element, String value) {
            String cleaned = value == null ? "" : escape(value).strip();
            if (!cleaned.isEmpty()) {
                out.append("  <").append(element).append('>').append(cleaned).append("</").append(element)
                        .append(">\n");
            }
        }

        void number(String element, int value) {
            out.append("  <").append(element).append('>').append(value).append("</").append(element).append(">\n");
        }

        void raw(String fragment) {
            out.append(fragment);
        }

        String build() {
            return out.append("</ComicInfo>\n").toString();
        }
    }

    /** Escapes markup characters and drops characters XML 1.0 cannot represent. */
    static String escape(String value) {
        StringBuilder out = new StringBuilder(value.length());
        value.codePoints().forEach(c -> {
            boolean legal = c == 0x9 || c == 0xA || c == 0xD || (c >= 0x20 && c <= 0xD7FF)
                    || (c >= 0xE000 && c <= 0xFFFD) || (c >= 0x10000 && c <= 0x10FFFF);
            if (!legal) {
                return;
            }
            switch (c) {
                case '&' -> out.append("&amp;");
                case '<' -> out.append("&lt;");
                case '>' -> out.append("&gt;");
                case '"' -> out.append("&quot;");
                case '\'' -> out.append("&apos;");
                default -> out.appendCodePoint(c);
            }
        });
        return out.toString();
    }
}
