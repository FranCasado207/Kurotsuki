package pt.yaku.i18n;

import java.text.MessageFormat;
import java.util.Locale;
import java.util.ResourceBundle;

public final class Messages {

    private static final String BASE_NAME = "messages";

    private static ResourceBundle bundle = load(Locale.ENGLISH);

    private Messages() {
    }

    public static void setLanguage(String language) {
        bundle = load(language == null || language.isBlank() ? Locale.ENGLISH : Locale.forLanguageTag(language));
    }

    public static String get(String key, Object... args) {
        String pattern = bundle.containsKey(key) ? bundle.getString(key) : key;
        return args.length == 0 ? pattern : MessageFormat.format(pattern, args);
    }

    private static ResourceBundle load(Locale locale) {
        return ResourceBundle.getBundle(BASE_NAME, locale,
                ResourceBundle.Control.getNoFallbackControl(ResourceBundle.Control.FORMAT_PROPERTIES));
    }
}
