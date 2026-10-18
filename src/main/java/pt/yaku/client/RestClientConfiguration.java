package pt.yaku.client;

import java.util.List;

import org.apache.cxf.jaxrs.client.JAXRSClientFactory;

import pt.yaku.booru.BooruClient;
import pt.yaku.booru.danbooru.DanbooruApi;
import pt.yaku.booru.danbooru.DanbooruClient;
import pt.yaku.booru.danbooru.DanbooruPost;
import pt.yaku.booru.danbooru.DanbooruTag;
import pt.yaku.booru.dapi.ApiPost;
import pt.yaku.booru.dapi.DapiListReader;
import pt.yaku.booru.dapi.DapiTag;
import pt.yaku.booru.gelbooru.GelbooruApi;
import pt.yaku.booru.gelbooru.GelbooruClient;
import pt.yaku.booru.safebooru.SafebooruApi;
import pt.yaku.booru.safebooru.SafebooruClient;
import pt.yaku.context.ApplicationContext;
import pt.yaku.user.config.BooruConfiguration;
import pt.yaku.user.config.BooruType;
import pt.yaku.user.config.preconfigured.DanbooruConfiguration;
import pt.yaku.user.config.preconfigured.GelbooruConfiguration;

public class RestClientConfiguration {

    private final ApplicationContext context;

    public RestClientConfiguration(ApplicationContext context) {
        this.context = context;
    }

    public <T> T create(Class<T> apiInterface, String baseUrl, List<?> providers) {
        return JAXRSClientFactory.create(baseUrl, apiInterface, providers);
    }

    public BooruClient clientFor(BooruConfiguration booru) {
        String baseUrl = booru.getBaseUrl();
        return switch (booru.getBooruType()) {
            case GELBOORU -> {
                String userId = booru instanceof GelbooruConfiguration gelbooru ? gelbooru.getUserId() : null;
                GelbooruApi api = create(GelbooruApi.class, baseUrl, List.of(
                        new DapiListReader<>("post", ApiPost.class),
                        new DapiListReader<>("tag", DapiTag.class)));
                yield new GelbooruClient(api, baseUrl, booru.getApiKey(), userId);
            }
            case SAFEBOORU -> {
                SafebooruApi api = create(SafebooruApi.class, baseUrl, List.of(
                        new DapiListReader<>("post", ApiPost.class)));
                yield new SafebooruClient(api, baseUrl);
            }
            case DANBOORU -> {
                String login = booru instanceof DanbooruConfiguration danbooru ? danbooru.getLogin() : null;
                DanbooruApi api = create(DanbooruApi.class, baseUrl, List.of(
                        new DapiListReader<>("", DanbooruPost.class),
                        new DapiListReader<>("", DanbooruTag.class)));
                yield new DanbooruClient(api, baseUrl, login, booru.getApiKey());
            }
            default -> throw new IllegalStateException("No client for booru type " + booru.getBooruType());
        };
    }

    public BooruClient selectedClient() {
        BooruConfiguration selected = context.getSelectedBooru();
        if (selected == null) {
            throw new IllegalStateException("No booru configuration selected.");
        }
        return clientFor(selected);
    }

    public static boolean hasClient(BooruType type) {
        return type == BooruType.GELBOORU || type == BooruType.SAFEBOORU || type == BooruType.DANBOORU;
    }
}
