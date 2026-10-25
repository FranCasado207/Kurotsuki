package pt.yaku.download;

import java.io.IOException;
import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.Set;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionException;
import java.util.function.Function;
import java.util.function.Supplier;
import java.util.function.DoubleConsumer;

import pt.yaku.booru.BooruClient;
import pt.yaku.booru.BooruHttp;
import pt.yaku.booru.Pool;
import pt.yaku.booru.Post;
import pt.yaku.cache.CacheManager;
import pt.yaku.context.ApplicationContext;
import pt.yaku.download.comic.ArchiveFormat;
import pt.yaku.download.comic.ArchiveWriter;
import pt.yaku.download.comic.ComicInfo;
import pt.yaku.i18n.Messages;
import pt.yaku.user.config.BooruConfiguration;

/** Saves a whole pool as one comic archive (cbz, zip, ...) containing the pages and a ComicInfo.xml. */
public class PoolArchiver {

    private static final Set<String> PAGE_TYPES = Set.of("png", "jpg", "jpeg", "gif", "bmp", "webp");
    private static final int PREFETCH = 4;
    private static final double FETCH_SHARE = 0.8;

    /** The booru a pool lives on: where to ask for its posts and how to describe it in the metadata. */
    public record Source(BooruClient client, String baseUrl, String publisher) {
    }

    private final Supplier<String> configuredFormat;
    private final Function<Pool, Source> sources;
    private final Function<String, CompletableFuture<Path>> fetcher;

    public PoolArchiver(ApplicationContext context) {
        this(() -> context.getUserConfig().getPoolArchiveFormat(), pool -> sourceFor(context, pool),
                url -> context.getCache().fetch(url));
    }

    /** @param sources returns null when the pool's booru is not configured or has no pools */
    PoolArchiver(Supplier<String> configuredFormat, Function<Pool, Source> sources,
                 Function<String, CompletableFuture<Path>> fetcher) {
        this.configuredFormat = configuredFormat;
        this.sources = sources;
        this.fetcher = fetcher;
    }

    /** The format from the config file; throws if it holds something we cannot write. */
    public ArchiveFormat configuredFormat() throws IOException {
        String configured = configuredFormat.get();
        if (configured == null || configured.isBlank()) {
            return ArchiveFormat.DEFAULT;
        }
        return ArchiveFormat.parse(configured).orElseThrow(() -> new IOException(
                Messages.get("download.formatUnknown", configured, String.join(", ", ArchiveFormat.extensions()))));
    }

    /** Blocking; run it off the FX thread. Returns the archive that was written. */
    public Path archive(Pool pool, Path directory, DoubleConsumer progress) throws IOException, InterruptedException {
        ArchiveFormat format = configuredFormat();
        Source source = sources.apply(pool);
        if (source == null) {
            throw new IOException(Messages.get("pools.notSupported"));
        }
        List<Post> posts = source.client().poolPosts(pool).stream().filter(PoolArchiver::isPage).toList();
        if (posts.isEmpty()) {
            throw new IOException(Messages.get("pools.postsEmpty"));
        }

        int width = Math.max(3, String.valueOf(posts.size()).length());
        List<ArchiveWriter.Entry> entries = new ArrayList<>();
        List<ComicInfo.Page> pages = new ArrayList<>();
        List<CompletableFuture<Path>> fetches = new ArrayList<>();
        for (int i = 0; i < posts.size(); i++) {
            // Keep a few downloads in flight, so a long pool does not wait on the network page by page.
            for (int ahead = fetches.size(); ahead < Math.min(posts.size(), i + PREFETCH); ahead++) {
                fetches.add(fetcher.apply(posts.get(ahead).getFileUrl()));
            }
            Post post = posts.get(i);
            Path file = await(fetches.get(i), i + 1);
            String extension = CacheManager.detectExtension(file, extensionOf(post));
            entries.add(new ArchiveWriter.Entry(String.format("%0" + width + "d.%s", i + 1, extension), file));
            pages.add(new ComicInfo.Page(post, Files.size(file)));
            progress.accept(FETCH_SHARE * (i + 1) / posts.size());
        }

        String base = source.baseUrl().replaceAll("/+$", "");
        String xml = ComicInfo.xml(pool, pages, base + "/pools/" + pool.id(), source.publisher(),
                "Kurotsuki " + BooruHttp.USER_AGENT.replaceAll("^kurotsuki/(\\S+).*$", "$1"));

        Files.createDirectories(directory);
        Path target = DownloadQueue.uniqueTarget(directory,
                DownloadQueue.sanitize(pool.displayName().replace('/', '_').replace('\\', '_'), "pool-" + pool.id()) + "." + format.getExtension());
        ArchiveWriter.write(format, entries, xml.getBytes(StandardCharsets.UTF_8), target,
                done -> progress.accept(FETCH_SHARE + (1 - FETCH_SHARE) * done));
        progress.accept(1.0);
        return target;
    }

    private static Path await(CompletableFuture<Path> fetch, int page) throws IOException, InterruptedException {
        try {
            return fetch.get();
        } catch (java.util.concurrent.ExecutionException e) {
            Throwable cause = e.getCause() instanceof CompletionException && e.getCause().getCause() != null
                    ? e.getCause().getCause() : e.getCause();
            throw new IOException(Messages.get("download.pageFailed", String.valueOf(page),
                    String.valueOf(cause.getMessage())), cause);
        }
    }

    private static Source sourceFor(ApplicationContext context, Pool pool) {
        List<BooruConfiguration> boorus = context.getUserConfig().getBooruConfigurations();
        if (boorus == null) {
            return null;
        }
        for (BooruConfiguration booru : boorus) {
            if (booru.getBaseUrl() != null && booru.getBaseUrl().equals(pool.booruBaseUrl())
                    && booru.supportsPools()) {
                return new Source(context.getRestConfig().clientFor(booru), booru.getBaseUrl(), publisherOf(booru));
            }
        }
        return null;
    }

    private static boolean isPage(Post post) {
        return post.getFileUrl() != null && PAGE_TYPES.contains(extensionOf(post));
    }

    private static String extensionOf(Post post) {
        String image = post.getImage();
        int dot = image == null ? -1 : image.lastIndexOf('.');
        return dot < 0 ? "" : image.substring(dot + 1).toLowerCase(Locale.ROOT);
    }

    private static String publisherOf(BooruConfiguration booru) {
        try {
            String host = URI.create(booru.getBaseUrl()).getHost();
            return host == null ? booru.getKindLabel() : host;
        } catch (IllegalArgumentException e) {
            return booru.getKindLabel();
        }
    }
}
