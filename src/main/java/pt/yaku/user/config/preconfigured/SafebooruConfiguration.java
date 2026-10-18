package pt.yaku.user.config.preconfigured;

import pt.yaku.user.config.BooruConfiguration;
import pt.yaku.user.config.BooruType;

public class SafebooruConfiguration extends BooruConfiguration {

    public SafebooruConfiguration() {
        setBooruType(BooruType.SAFEBOORU);
    }
}
