package pt.yaku.ui;

import java.io.IOException;
import java.io.UncheckedIOException;
import java.net.URI;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.sql.SQLException;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.CompletableFuture;
import java.util.function.Consumer;

import javafx.application.Platform;
import javafx.collections.ListChangeListener;
import javafx.geometry.Insets;
import javafx.geometry.Pos;
import javafx.scene.Node;
import javafx.scene.Scene;
import javafx.scene.control.Alert;
import javafx.scene.control.Button;
import javafx.scene.control.CheckMenuItem;
import javafx.scene.control.Label;
import javafx.scene.control.MenuButton;
import javafx.scene.control.MenuItem;
import javafx.scene.control.ProgressIndicator;
import javafx.scene.control.SeparatorMenuItem;
import javafx.scene.image.Image;
import javafx.scene.image.ImageView;
import javafx.scene.input.KeyCode;
import javafx.scene.layout.BorderPane;
import javafx.scene.layout.HBox;
import javafx.scene.layout.Priority;
import javafx.scene.layout.Region;
import javafx.scene.layout.StackPane;
import javafx.scene.layout.VBox;
import javafx.stage.Stage;
import pt.yaku.booru.BooruClient;
import pt.yaku.booru.Post;
import pt.yaku.booru.PostUrls;
import pt.yaku.booru.TagCategory;
import pt.yaku.cache.CacheManager;
import pt.yaku.context.ApplicationContext;
import pt.yaku.i18n.Messages;
import pt.yaku.library.Library;
import pt.yaku.library.PostFolder;
import pt.yaku.user.config.BooruConfiguration;

public class PostViewer extends Stage {

    private static final Set<String> STATIC_IMAGE_EXTENSIONS = Set.of("jpg", "jpeg", "png", "bmp");
    private static final Set<String> VIDEO_EXTENSIONS = Set.of("mp4", "webm", "mov", "m4v");
    private static final double FRAME_INSET_Y = 56;
    private static final double FRAME_INSET_X = 120;
    private static final double NAV_MARGIN = 16;
    private static final double MAX_DECODED_SIDE = 2048;

    private final ApplicationContext context;
    private final Library library;
    private final Consumer<String> onTagSearch;
    private final BooruConfiguration booru;
    private final List<Post> siblings;
    private int index;
    private Post post;
    private String pageUrl;

    private final StackPane center = new StackPane();
    private final StackPane mediaArea = new StackPane();
    private final Button previousButton = new Button("‹");
    private final Button nextButton = new Button("›");
    private final Label status = new Label();
    private final Label meta = new Label();
    private final Button downloadButton = new Button(Messages.get("common.download"));
    private final Button browserButton = new Button(Messages.get("viewer.openBrowser"));
    private final Button favoriteButton = new Button();
    private final MenuButton folderButton = new MenuButton(Messages.get("grid.addToFolder"));
    private final TagPanel tagPanel;
    private int generation;
    private Path cachedFile;
    private Node media;

    public PostViewer(Stage owner, ApplicationContext context, Library library,
                      Post post, List<Post> siblings, Consumer<String> onTagSearch, BooruConfiguration booru) {
        this.context = context;
        this.library = library;
        this.onTagSearch = onTagSearch;
        this.booru = booru;
        this.siblings = siblings.contains(post) ? List.copyOf(siblings) : List.of(post);
        this.index = this.siblings.indexOf(post);
        this.post = post;

        initOwner(owner);

        center.getStyleClass().add("viewer-stage");
        mediaArea.getChildren().add(loadingView());
        previousButton.getStyleClass().add("viewer-nav");
        nextButton.getStyleClass().add("viewer-nav");
        previousButton.setOnAction(event -> move(-1));
        nextButton.setOnAction(event -> move(1));
        StackPane.setAlignment(previousButton, Pos.CENTER_LEFT);
        StackPane.setAlignment(nextButton, Pos.CENTER_RIGHT);
        StackPane.setMargin(previousButton, new Insets(0, 0, 0, NAV_MARGIN));
        StackPane.setMargin(nextButton, new Insets(0, NAV_MARGIN, 0, 0));
        center.getChildren().addAll(mediaArea, previousButton, nextButton);

        downloadButton.getStyleClass().add("accent");
        downloadButton.setDisable(true);
        downloadButton.setOnAction(event -> downloadToFolder());
        browserButton.setOnAction(event -> openInBrowser(pageUrl));
        favoriteButton.setOnAction(event -> library.favorites().togglePost(this.post));
        library.favorites().getPosts().addListener((ListChangeListener<Post>) change -> updateFavoriteButton());
        folderButton.setOnShowing(event -> rebuildFolderMenu());
        status.getStyleClass().add("viewer-status");
        meta.getStyleClass().add("viewer-meta");

        Region spacer = new Region();
        HBox.setHgrow(spacer, Priority.ALWAYS);
        HBox bottom = new HBox(12, downloadButton, browserButton, favoriteButton, folderButton, status, spacer, meta);
        bottom.setAlignment(Pos.CENTER_LEFT);
        bottom.getStyleClass().add("viewer-toolbar");

        BorderPane root = new BorderPane();
        root.setCenter(center);
        root.setBottom(bottom);
        tagPanel = new TagPanel(post, library.favorites(), onTagSearch);
        VBox side = new VBox(tagPanel);
        side.getStyleClass().add("viewer-side");
        VBox.setVgrow(tagPanel, Priority.ALWAYS);
        root.setLeft(side);

        Scene scene = new Scene(root, 1150, 720);
        Styles.apply(scene);
        scene.setOnKeyPressed(event -> {
            if (event.getCode() == KeyCode.LEFT) {
                move(-1);
            } else if (event.getCode() == KeyCode.RIGHT) {
                move(1);
            }
        });
        setScene(scene);
        setOnHidden(event -> cleanup());

        showPost(post);
        show();
    }

    private void move(int delta) {
        int target = index + delta;
        if (target < 0 || target >= siblings.size()) {
            return;
        }
        index = target;
        showPost(siblings.get(index));
    }

    private void showPost(Post next) {
        cleanup();
        generation++;
        post = next;
        media = null;
        cachedFile = null;
        setTitle(Messages.get("viewer.title", String.valueOf(post.getId())));
        pageUrl = booru == null ? null : PostUrls.pageUrl(booru.getEngine(), booru.getFlavor(),
                booru.getBaseUrl(), post.getId());
        browserButton.setDisable(pageUrl == null);
        downloadButton.setDisable(true);
        meta.setText("");
        setStatus("", false);
        previousButton.setDisable(index <= 0);
        nextButton.setDisable(index >= siblings.size() - 1);
        mediaArea.getChildren().setAll(loadingView());
        tagPanel.setPost(post);
        updateFavoriteButton();

        resolveTagCategories(booru);
        loadOriginal();
    }

    private void resolveTagCategories(BooruConfiguration booru) {
        Post requested = post;
        int gen = generation;
        List<String> missing = new ArrayList<>();
        for (String tag : TagPanel.splitTags(requested.getTags())) {
            if (!requested.getTagCategories().containsKey(tag)) {
                missing.add(tag);
            }
        }
        if (missing.isEmpty() || booru == null) {
            return;
        }
        try {
            Map<String, TagCategory> cached = library.store().tagCategories(requested.getBooruBaseUrl(), missing);
            applyCategories(requested, cached);
            List<String> unknown = missing.stream().filter(tag -> !cached.containsKey(tag)).toList();
            if (unknown.isEmpty()) {
                return;
            }
            BooruClient client = context.getRestConfig().clientFor(booru);
            CompletableFuture.supplyAsync(() -> client.tagCategories(unknown))
                    .whenComplete((found, error) -> Platform.runLater(() -> {
                        if (gen != generation) {
                            return;
                        }
                        if (error != null) {
                            setStatus(Messages.get("viewer.categoriesError", String.valueOf(error.getMessage())), true);
                            return;
                        }
                        Map<String, TagCategory> resolved = new LinkedHashMap<>();
                        for (String tag : unknown) {
                            resolved.put(tag, found.getOrDefault(tag, TagCategory.GENERAL));
                        }
                        try {
                            library.store().saveTagCategories(requested.getBooruBaseUrl(), resolved);
                        } catch (SQLException e) {
                            setStatus(Messages.get("viewer.categoriesError", e.getMessage()), true);
                        }
                        applyCategories(requested, resolved);
                    }));
        } catch (SQLException | IllegalStateException e) {
            setStatus(Messages.get("viewer.categoriesError", e.getMessage()), true);
        }
    }

    private void applyCategories(Post target, Map<String, TagCategory> categories) {
        if (categories.isEmpty()) {
            return;
        }
        target.getTagCategories().putAll(categories);
        if (target == post) {
            tagPanel.rebuild();
        }
    }

    private void loadOriginal() {
        Post requested = post;
        int gen = generation;
        String url = requested.getFileUrl();
        if (url == null) {
            showError(Messages.get("viewer.noFile"), false);
            return;
        }

        String urlExtension = extensionOf(url);
        CacheManager.fetch(url)
                .thenApplyAsync(file -> prepare(file, CacheManager.detectExtension(file, urlExtension)))
                .whenComplete((prepared, error) -> Platform.runLater(() -> {
                    if (gen != generation) {
                        return;
                    }
                    if (error != null) {
                        showError(Messages.get("viewer.loadError", error.getCause().getMessage()), true);
                        return;
                    }
                    cachedFile = prepared.file();
                    showMedia(prepared);
                    downloadButton.setDisable(false);
                    meta.setText(metaText(requested));
                }));
    }

    private static String metaText(Post post) {
        List<String> parts = new ArrayList<>();
        if (post.getRating() != null && !post.getRating().isBlank()) {
            parts.add(post.getRating());
        }
        if (post.getWidth() > 0 && post.getHeight() > 0) {
            parts.add(post.getWidth() + " × " + post.getHeight());
        }
        return String.join("  ·  ", parts);
    }

    private record Prepared(Path file, String extension, List<AnimatedGifView.Frame> gifFrames) {
    }

    private static Prepared prepare(Path file, String extension) {
        if ("gif".equals(extension) || "zip".equals(extension)) {
            try {
                List<AnimatedGifView.Frame> frames = "zip".equals(extension)
                        ? UgoiraDecoder.decode(file)
                        : AnimatedGifView.decode(file);
                return new Prepared(file, extension, frames);
            } catch (IOException e) {
                throw new UncheckedIOException(e);
            }
        }
        return new Prepared(file, extension, null);
    }

    private void showMedia(Prepared prepared) {
        Path file = prepared.file();
        String extension = prepared.extension();
        if (STATIC_IMAGE_EXTENSIONS.contains(extension)) {
            Image image = new Image(file.toUri().toString(), MAX_DECODED_SIDE, MAX_DECODED_SIDE,
                    true, true, true);
            image.errorProperty().addListener((obs, wasError, isError) -> {
                if (isError) {
                    showUnsupported(Messages.get("viewer.imageError"), file);
                }
            });
            ImageView view = new ImageView(image);
            view.setPreserveRatio(true);
            view.fitWidthProperty().bind(center.widthProperty().subtract(FRAME_INSET_X));
            view.fitHeightProperty().bind(center.heightProperty().subtract(FRAME_INSET_Y));
            setMedia(view);
        } else if ("gif".equals(extension) || "zip".equals(extension)) {
            try {
                AnimatedGifView gif = new AnimatedGifView(prepared.gifFrames());
                gif.fitTo(center.widthProperty(), center.heightProperty(), FRAME_INSET_X, FRAME_INSET_Y);
                setMedia(gif);
            } catch (RuntimeException e) {
                showUnsupported(Messages.get("viewer.gifError"), file);
            }
        } else if (VIDEO_EXTENSIONS.contains(extension)) {
            try {
                setMedia(new VideoPlayer(file, () -> showUnsupported(Messages.get("viewer.videoError"), file)));
            } catch (RuntimeException e) {
                showUnsupported(Messages.get("viewer.videoError"), file);
            }
        } else {
            showUnsupported(Messages.get("viewer.noViewer", extension), file);
        }
    }

    private void setMedia(Node node) {
        media = node;
        StackPane frame = new StackPane(node);
        frame.getStyleClass().add("viewer-frame");
        mediaArea.getChildren().setAll(frame);
    }

    private void showUnsupported(String message, Path file) {
        Label text = new Label(message);
        text.getStyleClass().add("viewer-status");
        Button openButton = new Button(Messages.get("viewer.openExternal"));
        openButton.setOnAction(event -> openExternally(file));
        mediaArea.getChildren().setAll(messageCard(text, openButton));
        setStatus(message, true);
    }

    private void openExternally(Path file) {
        try {
            SystemOpener.openFile(file);
        } catch (IOException e) {
            setStatus(Messages.get("viewer.openError", e.getMessage()), true);
        }
    }

    private void updateFavoriteButton() {
        favoriteButton.setText(library.favorites().isPostFavorite(post.key())
                ? Messages.get("common.unfavorite") : Messages.get("common.favorite"));
    }

    private void rebuildFolderMenu() {
        Post current = post;
        List<MenuItem> items = new ArrayList<>();
        for (PostFolder folder : library.folders().getFolders()) {
            CheckMenuItem item = new CheckMenuItem(folder.getName());
            item.setSelected(folder.contains(current.key()));
            item.setOnAction(event -> {
                if (item.isSelected()) {
                    library.folders().addPost(folder, current);
                } else {
                    library.folders().removePost(folder, current);
                }
            });
            items.add(item);
        }
        if (!items.isEmpty()) {
            items.add(new SeparatorMenuItem());
        }
        MenuItem newFolder = new MenuItem(Messages.get("folders.new"));
        newFolder.setOnAction(event -> Dialogs.askText(this,
                        Messages.get("folders.newTitle"), Messages.get("folders.name"))
                .ifPresent(name -> library.folders().addPost(library.folders().create(name), current)));
        items.add(newFolder);
        folderButton.getItems().setAll(items);
    }

    private void openInBrowser(String url) {
        if (url == null) {
            return;
        }
        try {
            SystemOpener.openUrl(url);
        } catch (IOException | IllegalArgumentException e) {
            setStatus(Messages.get("viewer.browserError", e.getMessage()), true);
        }
    }

    private void downloadToFolder() {
        String folder = context.getUserConfig().getDownloadFolder();
        if (folder == null || folder.isBlank()) {
            Styles.alert(Alert.AlertType.WARNING,
                    Messages.get("download.folderNotSet")).showAndWait();
            return;
        }

        String fileName = post.getImage() != null ? post.getImage() : cachedFile.getFileName().toString();
        Path target = Path.of(folder, fileName);
        try {
            Files.createDirectories(target.getParent());
            if (UgoiraGif.isUgoiraZip(target)) {
                target = UgoiraGif.convert(cachedFile, UgoiraGif.gifNameFor(target));
            } else {
                Files.copy(cachedFile, target, StandardCopyOption.REPLACE_EXISTING);
            }
            setStatus(Messages.get("viewer.saved", String.valueOf(target)), false);
        } catch (IOException e) {
            setStatus(Messages.get("viewer.saveError", e.getMessage()), true);
        }
    }

    private void showError(String message, boolean retryable) {
        Label text = new Label(message);
        text.getStyleClass().add("viewer-error");
        text.setWrapText(true);
        if (!retryable) {
            mediaArea.getChildren().setAll(messageCard(text));
            return;
        }
        Button retry = new Button(Messages.get("viewer.retry"));
        retry.setOnAction(event -> {
            mediaArea.getChildren().setAll(loadingView());
            loadOriginal();
        });
        mediaArea.getChildren().setAll(messageCard(text, retry));
    }

    private void setStatus(String message, boolean error) {
        status.getStyleClass().remove("viewer-error");
        if (error) {
            status.getStyleClass().add("viewer-error");
        }
        status.setText(message);
    }

    private static Node loadingView() {
        ProgressIndicator indicator = new ProgressIndicator();
        Label text = new Label(Messages.get("viewer.loading"));
        text.getStyleClass().add("viewer-status");
        return messageCard(indicator, text);
    }

    private static VBox messageCard(Node... children) {
        VBox card = new VBox(12, children);
        card.setAlignment(Pos.CENTER);
        return card;
    }

    private void cleanup() {
        if (media instanceof AnimatedGifView gif) {
            gif.stop();
        } else if (media instanceof VideoPlayer video) {
            video.dispose();
        }
    }

    private static String extensionOf(String url) {
        String path = URI.create(url).getPath();
        int dot = path.lastIndexOf('.');
        return dot >= 0 ? path.substring(dot + 1).toLowerCase() : "bin";
    }
}
