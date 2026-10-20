package pt.yaku.user.config;

import lombok.Getter;
import lombok.NoArgsConstructor;
import lombok.Setter;
import pt.yaku.booru.BooruEngine;
import pt.yaku.booru.BooruFlavor;
import pt.yaku.booru.BooruTemplate;

@Getter
@Setter
@NoArgsConstructor
public class BooruConfiguration {
    BooruTemplate template;
    BooruEngine engine;
    BooruFlavor flavor;
    String customBaseUrl;
    String apiKey;
    String userId;

    public BooruEngine getEngine() {
        return template != null ? template.getEngine() : engine;
    }

    public BooruFlavor getFlavor() {
        return template != null ? template.getFlavor() : flavor;
    }

    public String getBaseUrl() {
        if (customBaseUrl != null) {
            return customBaseUrl;
        }
        return template != null ? template.getDefaultBaseUrl() : null;
    }

    public String getKindLabel() {
        if (template != null) {
            return template.getLabel();
        }
        return engine != null ? engine.getLabel() : "Custom";
    }

    public void validate() {
        if (getBaseUrl() == null || getBaseUrl().isBlank()) {
            throw new IllegalStateException("Booru without baseUrl: " + getKindLabel());
        }
    }
}
