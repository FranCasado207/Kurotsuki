package pt.yaku.client;

import java.util.List;

import org.apache.cxf.jaxrs.client.JAXRSClientFactory;

import pt.yaku.booru.BooruClient;
import pt.yaku.booru.BooruEngine;
import pt.yaku.booru.BooruFlavor;
import pt.yaku.booru.danbooru.DanbooruApi;
import pt.yaku.booru.danbooru.DanbooruClient;
import pt.yaku.booru.danbooru.DanbooruPost;
import pt.yaku.booru.danbooru.DanbooruTag;
import pt.yaku.booru.dapi.ApiPost;
import pt.yaku.booru.dapi.DapiListReader;
import pt.yaku.booru.gelbooru.GelbooruFamilyApi;
import pt.yaku.booru.gelbooru.GelbooruFamilyClient;
import pt.yaku.booru.hydrus.HydrusApi;
import pt.yaku.booru.hydrus.HydrusClient;
import pt.yaku.booru.e621.E621Api;
import pt.yaku.booru.e621.E621Client;
import pt.yaku.booru.moebooru.MoebooruApi;
import pt.yaku.booru.moebooru.MoebooruClient;
import pt.yaku.booru.shimmie.ShimmieApi;
import pt.yaku.booru.shimmie.ShimmieClient;
import pt.yaku.booru.szurubooru.SzurubooruApi;
import pt.yaku.booru.szurubooru.SzurubooruClient;
import pt.yaku.context.ApplicationContext;
import pt.yaku.user.config.BooruConfiguration;

public class RestClientConfiguration {

    private final ApplicationContext context;

    public RestClientConfiguration(ApplicationContext context) {
        this.context = context;
    }

    public <T> T create(Class<T> apiInterface, String baseUrl, List<?> providers) {
        return JAXRSClientFactory.create(baseUrl, apiInterface, providers);
    }

    public BooruClient clientFor(BooruConfiguration booru) {
        BooruEngine engine = booru.getEngine();
        if (engine == null) {
            throw new IllegalStateException("No engine selected for this booru.");
        }
        String baseUrl = booru.getBaseUrl();
        return switch (engine) {
            case GELBOORU_FAMILY -> {
                GelbooruFamilyApi api = create(GelbooruFamilyApi.class, baseUrl, List.of(
                        new DapiListReader<>("post", ApiPost.class)));
                yield new GelbooruFamilyClient(api, baseUrl, booru.getApiKey(), booru.getUserId());
            }
            case DANBOORU_SOFTWARE -> {
                DanbooruApi api = create(DanbooruApi.class, baseUrl, List.of(
                        new DapiListReader<>("", DanbooruPost.class),
                        new DapiListReader<>("", DanbooruTag.class)));
                yield new DanbooruClient(api, baseUrl, booru.getUserId(), booru.getApiKey());
            }
            case MOEBOORU_SHIMMIE -> {
                BooruFlavor flavor = booru.getFlavor();
                if (flavor == null) {
                    throw new IllegalStateException("Choose a flavor (Moebooru or Shimmie) for this booru.");
                }
                if (flavor == BooruFlavor.SHIMMIE) {
                    ShimmieApi api = create(ShimmieApi.class, baseUrl, List.of());
                    yield new ShimmieClient(api, baseUrl);
                }
                MoebooruApi api = create(MoebooruApi.class, baseUrl, List.of());
                yield new MoebooruClient(api, baseUrl);
            }
            case E621 -> {
                E621Api api = create(E621Api.class, baseUrl, List.of());
                yield new E621Client(api, baseUrl, booru.getUserId(), booru.getApiKey());
            }
            case HYDRUS -> {
                HydrusApi api = create(HydrusApi.class, baseUrl, List.of());
                yield new HydrusClient(api, baseUrl, booru.getApiKey());
            }
            case SZURUBOORU -> {
                SzurubooruApi api = create(SzurubooruApi.class, baseUrl, List.of());
                yield new SzurubooruClient(api, baseUrl, booru.getUserId(), booru.getApiKey());
            }
        };
    }

    public BooruClient selectedClient() {
        BooruConfiguration selected = context.getSelectedBooru();
        if (selected == null) {
            throw new IllegalStateException("No booru configuration selected.");
        }
        return clientFor(selected);
    }

    public static boolean hasClient(BooruConfiguration booru) {
        return booru.getEngine() != null && booru.getEngine().isImplemented();
    }
}
