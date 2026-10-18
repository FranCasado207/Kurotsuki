package pt.yaku.user.config;

import lombok.Getter;
import lombok.NoArgsConstructor;
import lombok.Setter;

@Getter
@Setter
@NoArgsConstructor
public class BooruConfiguration {
    BooruType booruType = BooruType.GENERIC;
    String customBaseUrl;
    String apiKey;

    public String getBaseUrl() {
        return customBaseUrl != null ? customBaseUrl : booruType.getDefaultBaseUrl();
    }

    public void validate() {
        if (getBaseUrl() == null || getBaseUrl().isBlank()) {
            throw new IllegalStateException("Booru without baseUrl: " + booruType);
        }
    }
}
