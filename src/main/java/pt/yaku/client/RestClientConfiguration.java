package pt.yaku.client;

import java.util.List;

import org.apache.cxf.jaxrs.client.JAXRSClientFactory;

import pt.yaku.context.ApplicationContext;
import pt.yaku.user.config.BooruConfiguration;
import pt.yaku.user.config.preconfigured.GelbooruConfiguration;

public class RestClientConfiguration {

    private final ApplicationContext context;

    public RestClientConfiguration(ApplicationContext context) {
        this.context = context;
    }

    public <T> T create(Class<T> apiInterface, String baseUrl, List<?> providers) {
        return JAXRSClientFactory.create(baseUrl, apiInterface, providers);
    }

    public GelbooruApi gelbooru(BooruConfiguration booru) {
        return create(GelbooruApi.class, booru.getBaseUrl(), List.of(
                new GelbooruListReader<>("post", GelbooruPost.class),
                new GelbooruListReader<>("tag", GelbooruTag.class)));
    }

    public GelbooruApi selectedGelbooru() {
        BooruConfiguration selected = context.getSelectedBooru();
        if (selected == null) {
            throw new IllegalStateException("No booru configuration selected.");
        }
        if (!(selected instanceof GelbooruConfiguration)) {
            throw new IllegalStateException("The selected configuration is not Gelbooru: " + selected.getBooruType());
        }
        return gelbooru(selected);
    }
}
