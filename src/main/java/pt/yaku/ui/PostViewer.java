package pt.yaku.ui;

import java.awt.Desktop;
import java.io.IOException;
import java.io.UncheckedIOException;
import java.net.URI;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.util.Set;
import java.util.List;
import java.util.function.Consumer;

import javafx.application.Platform;
import javafx.geometry.Insets;
import javafx.scene.Node;
import javafx.scene.Scene;
import javafx.scene.control.Alert;
import javafx.scene.control.Button;
import javafx.scene.control.Label;
import javafx.scene.control.ProgressIndicator;
import javafx.scene.image.Image;
import javafx.scene.image.ImageView;
import javafx.scene.layout.BorderPane;
import javafx.scene.layout.HBox;
import javafx.scene.layout.StackPane;
import javafx.stage.Stage;
import pt.yaku.cache.CacheManager;
import pt.yaku.client.GelbooruPost;
import pt.yaku.context.ApplicationContext;
import pt.yaku.favorites.Favorites;
import pt.yaku.i18n.Messages;

public class PostViewer extends Stage {

    private static final Set<String> STATIC_IMAGE_EXTENSIONS = Set.of("jpg", "jpeg", "png", "bmp");
    private static final Set<String> VIDEO_EXTENSIONS = Set.of("mp4", "webm", "mov", "m4v");

    private final ApplicationContext context;
    private final GelbooruPost post;
    private final StackPane center = new StackPane();
    private final Label status = new Label(Messages.get("viewer.loading"));
    private final Button downloadButton = new Button(Messages.get("common.download"));
    private Path cachedFile;
    private Node media;

    public PostViewer(Stage owner, ApplicationContext context, Favorites favorites,
                      GelbooruPost post, Consumer<String> onTagSearch) {
        this.context = context;
        this.post = post;

        initOwner(owner);
        setTitle(Messages.get("viewer.title", String.valueOf(post.getId())));

        center.setPadding(new Insets(10));
        center.getChildren().add(new ProgressIndicator());

        downloadButton.setDisable(true);
        downloadButton.setOnAction(event -> downloadToFolder());
        HBox bottom = new HBox(10, downloadButton, status);
        bottom.setPadding(new Insets(10));

        BorderPane root = new BorderPane();
        root.setCenter(center);
        root.setBottom(bottom);
        root.setRight(new TagPanel(post, favorites, onTagSearch));

        setScene(new Scene(root, 1150, 720));
        setOnHidden(event -> cleanup());
        show();

        loadOriginal();
    }

    private void loadOriginal() {
        String url = post.getFileUrl();
        if (url == null) {
            showError(Messages.get("viewer.noFile"));
            return;
        }

        String urlExtension = extensionOf(url);
        CacheManager.fetch(url)
                .thenApplyAsync(file -> prepare(file, CacheManager.detectExtension(file, urlExtension)))
                .whenComplete((prepared, error) -> Platform.runLater(() -> {
                    if (error != null) {
                        showError(Messages.get("viewer.loadError", error.getCause().getMessage()));
                        return;
                    }
                    cachedFile = prepared.file();
                    showMedia(prepared);
                    downloadButton.setDisable(false);
                    status.setText(post.getRating() + " - " + post.getWidth() + "x" + post.getHeight());
                }));
    }

    private record Prepared(Path file, String extension, List<AnimatedGifView.Frame> gifFrames) {
    }

    private static Prepared prepare(Path file, String extension) {
        if ("gif".equals(extension)) {
            try {
                return new Prepared(file, extension, AnimatedGifView.decode(file));
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
            ImageView view = new ImageView(new Image(file.toUri().toString()));
            view.setPreserveRatio(true);
            view.fitWidthProperty().bind(center.widthProperty().subtract(20));
            view.fitHeightProperty().bind(center.heightProperty().subtract(20));
            setMedia(view);
        } else if ("gif".equals(extension)) {
            try {
                setMedia(new AnimatedGifView(prepared.gifFrames()));
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
        center.getChildren().setAll(node);
    }

    private void showUnsupported(String message, Path file) {
        Button openButton = new Button(Messages.get("viewer.openExternal"));
        openButton.setOnAction(event -> openExternally(file));
        center.getChildren().setAll(new HBox(10, new Label(message), openButton));
        status.setText(message);
    }

    private void openExternally(Path file) {
        try {
            Desktop.getDesktop().open(file.toFile());
        } catch (IOException | UnsupportedOperationException e) {
            showError(Messages.get("viewer.openError", e.getMessage()));
        }
    }

    private void downloadToFolder() {
        String folder = context.getUserConfig().getDownloadFolder();
        if (folder == null || folder.isBlank()) {
            new Alert(Alert.AlertType.WARNING,
                    Messages.get("download.folderNotSet")).showAndWait();
            return;
        }

        String fileName = post.getImage() != null ? post.getImage() : cachedFile.getFileName().toString();
        Path target = Path.of(folder, fileName);
        try {
            Files.createDirectories(target.getParent());
            Files.copy(cachedFile, target, StandardCopyOption.REPLACE_EXISTING);
            status.setText(Messages.get("viewer.saved", String.valueOf(target)));
        } catch (IOException e) {
            showError(Messages.get("viewer.saveError", e.getMessage()));
        }
    }

    private void showError(String message) {
        center.getChildren().setAll(new Label(message));
        status.setText(message);
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
