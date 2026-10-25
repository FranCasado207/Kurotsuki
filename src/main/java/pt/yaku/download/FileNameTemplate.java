package pt.yaku.download;

import java.net.URI;
import java.util.ArrayList;
import java.util.List;
import java.util.function.Function;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

import pt.yaku.booru.Post;
import pt.yaku.booru.TagCategory;

/**
 * Turns a template such as {@code %booru%/%artist%/%id%_%md5%.%ext%} into a relative path for a post. Every
 * substituted value is sanitised on its own, so a hostile tag or file name can neither add path separators nor
 * climb out of the download folder; only the {@code /} typed in the template itself separates folders.
 */
public final class FileNameTemplate {

    public static final String DEFAULT = "%file%";
    public static final String TOKENS = "%booru% %artist% %group% %id% %md5% %file% %name% %ext% %rating% %score%";

    private static final Pattern TOKEN = Pattern.compile("%([a-z0-9]+)%");
    private static final Pattern MD5 = Pattern.compile("[0-9a-fA-F]{32}");

    private final String template;
    private final Function<Post, String> groupOf;

    public FileNameTemplate(String template, Function<Post, String> groupOf) {
        this.template = template == null || template.isBlank() ? DEFAULT : template.trim();
        this.groupOf = groupOf == null ? post -> "" : groupOf;
    }

    /** The relative path (forward slashes) for the post; never empty, never absolute, never contains "..". */
    public String render(Post post) {
        String original = DownloadQueue.safeFileName(post);
        List<String> segments = new ArrayList<>();
        for (String part : template.split("[/\\\\]")) {
            String value = DownloadQueue.sanitize(substitute(part, post, original), "");
            if (!value.isEmpty()) {
                segments.add(value);
            }
        }
        if (segments.isEmpty()) {
            return original;
        }
        // A template without %ext% or %file% would otherwise produce files with no extension.
        String last = segments.get(segments.size() - 1);
        if (!last.contains(".") && !template.contains("%ext%") && !template.contains("%file%")) {
            String extension = extensionOf(original);
            if (!extension.isEmpty()) {
                segments.set(segments.size() - 1, last + "." + extension);
            }
        }
        return String.join("/", segments);
    }

    private String substitute(String part, Post post, String original) {
        Matcher matcher = TOKEN.matcher(part);
        StringBuilder out = new StringBuilder();
        while (matcher.find()) {
            matcher.appendReplacement(out, Matcher.quoteReplacement(clean(valueOf(matcher.group(1), post, original))));
        }
        matcher.appendTail(out);
        return out.toString();
    }

    private String valueOf(String token, Post post, String original) {
        return switch (token) {
            case "booru" -> hostOf(post.getBooruBaseUrl());
            case "artist" -> artistOf(post);
            case "group" -> groupOf.apply(post);
            case "id" -> String.valueOf(post.getId());
            case "md5" -> md5Of(original, post);
            case "file" -> original;
            case "name" -> stemOf(original);
            case "ext" -> extensionOf(original);
            case "rating" -> post.getRating() == null ? "" : post.getRating();
            case "score" -> String.valueOf(post.getScore());
            default -> "%" + token + "%";
        };
    }

    /** Separators inside a value would add folders the template did not ask for. */
    private static String clean(String value) {
        return value == null ? "" : value.replace('/', '_').replace('\\', '_');
    }

    static String artistOf(Post post) {
        if (post.getTagCategories() != null) {
            for (var entry : post.getTagCategories().entrySet()) {
                if (entry.getValue() == TagCategory.ARTIST) {
                    return entry.getKey();
                }
            }
        }
        return "unknown";
    }

    private static String md5Of(String original, Post post) {
        String stem = stemOf(original);
        return MD5.matcher(stem).matches() ? stem.toLowerCase() : String.valueOf(post.getId());
    }

    static String stemOf(String fileName) {
        int dot = fileName.lastIndexOf('.');
        return dot > 0 ? fileName.substring(0, dot) : fileName;
    }

    static String extensionOf(String fileName) {
        int dot = fileName.lastIndexOf('.');
        return dot > 0 && dot < fileName.length() - 1 ? fileName.substring(dot + 1) : "";
    }

    static String hostOf(String baseUrl) {
        if (baseUrl == null) {
            return "";
        }
        try {
            String host = URI.create(baseUrl).getHost();
            return host == null ? "" : host;
        } catch (IllegalArgumentException e) {
            return "";
        }
    }
}
