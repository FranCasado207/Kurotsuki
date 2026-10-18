package pt.yaku.user.config.preconfigured;

import lombok.Getter;
import lombok.Setter;
import pt.yaku.user.config.BooruConfiguration;
import pt.yaku.user.config.BooruType;

@Getter
@Setter
public class GelbooruConfiguration extends BooruConfiguration {
    String userId;

    public GelbooruConfiguration() {
        setBooruType(BooruType.GELBOORU);
    }

}
