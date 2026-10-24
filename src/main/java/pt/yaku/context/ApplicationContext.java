package pt.yaku.context;

import java.io.IOException;

import lombok.AccessLevel;
import lombok.Getter;
import lombok.Setter;
import pt.yaku.cache.CacheManager;
import pt.yaku.client.MediaDownloader;
import pt.yaku.client.RequestAuth;
import pt.yaku.client.RestClientConfiguration;
import pt.yaku.ui.ImageLoader;
import pt.yaku.user.config.AppPaths;
import pt.yaku.user.config.BooruConfiguration;
import pt.yaku.user.config.ConfigManager;
import pt.yaku.user.config.UserConfiguration;

@Getter
@Setter
public class ApplicationContext {
    private UserConfiguration userConfig;
    private RestClientConfiguration restConfig;
    private BooruConfiguration selectedBooru;
    @Setter(AccessLevel.NONE)
    private final CacheManager cache;
    @Setter(AccessLevel.NONE)
    private final MediaDownloader downloader;
    @Setter(AccessLevel.NONE)
    private final ImageLoader imageLoader;

    public ApplicationContext() throws IOException{
        this.userConfig = ConfigManager.configLoader();
        this.restConfig = new RestClientConfiguration(this);
        // These read the configuration lazily, so settings changes (cache folder, boorus) apply immediately.
        this.downloader = new MediaDownloader(new RequestAuth(() -> getUserConfig().getBooruConfigurations()));
        this.cache = new CacheManager(() -> getUserConfig().getCacheFolder(), AppPaths.get().cache(),
                downloader::download);
        this.imageLoader = new ImageLoader(cache);
    }

    public void saveUserConfig() throws IOException {
        ConfigManager.saveConfig(userConfig);
    }
}
