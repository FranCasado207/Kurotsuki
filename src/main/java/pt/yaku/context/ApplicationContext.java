package pt.yaku.context;

import java.io.IOException;

import lombok.AllArgsConstructor;
import lombok.Getter;
import lombok.Setter;
import pt.yaku.client.RestClientConfiguration;
import pt.yaku.user.config.BooruConfiguration;
import pt.yaku.user.config.ConfigManager;
import pt.yaku.user.config.UserConfiguration;

@Getter
@Setter
@AllArgsConstructor
public class ApplicationContext {
    private UserConfiguration userConfig;
    private RestClientConfiguration restConfig;
    private BooruConfiguration selectedBooru;

    public ApplicationContext() throws IOException{
        this.userConfig = ConfigManager.configLoader();
        this.restConfig = new RestClientConfiguration(this);
    }

    public void saveUserConfig() throws IOException {
        ConfigManager.saveConfig(userConfig);
    }
}
