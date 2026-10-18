package pt.yaku.user.config;

import pt.yaku.user.config.preconfigured.GelbooruConfiguration;

public enum BooruType {
    GENERIC(null, BooruConfiguration.class),
    GELBOORU("https://gelbooru.com", GelbooruConfiguration.class);

    private final String defaultBaseUrl;
    private final Class<? extends BooruConfiguration> configClass;

    BooruType(String defaultBaseUrl, Class<? extends BooruConfiguration> configClass) {
        this.defaultBaseUrl = defaultBaseUrl;
        this.configClass = configClass;
    }

    public String getDefaultBaseUrl() {
        return defaultBaseUrl;
    }

    public Class<? extends BooruConfiguration> getConfigClass() {
        return configClass;
    }
}
