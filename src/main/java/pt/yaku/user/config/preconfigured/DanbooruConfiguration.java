package pt.yaku.user.config.preconfigured;

import lombok.Getter;
import lombok.Setter;
import pt.yaku.user.config.BooruConfiguration;
import pt.yaku.user.config.BooruType;

@Getter
@Setter
public class DanbooruConfiguration extends BooruConfiguration {

    String login;

    public DanbooruConfiguration() {
        setBooruType(BooruType.DANBOORU);
    }
}
