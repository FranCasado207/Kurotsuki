package pt.yaku.ui;

import java.awt.Graphics2D;
import java.awt.image.BufferedImage;
import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.UncheckedIOException;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Iterator;
import java.util.List;

import javax.imageio.ImageIO;
import javax.imageio.ImageReader;
import javax.imageio.metadata.IIOMetadata;
import javax.imageio.metadata.IIOMetadataNode;
import javax.imageio.stream.ImageInputStream;

import javafx.animation.Animation;
import javafx.animation.KeyFrame;
import javafx.animation.Timeline;
import javafx.scene.image.Image;
import javafx.scene.image.ImageView;
import javafx.scene.layout.StackPane;
import javafx.util.Duration;
import org.w3c.dom.Node;
import org.w3c.dom.NodeList;

public class AnimatedGifView extends StackPane {

    private static final int MIN_DELAY_MS = 20;
    private static final int BATCH_SIZE = Runtime.getRuntime().availableProcessors();

    private final ImageView view = new ImageView();
    private final Timeline timeline = new Timeline();

    public AnimatedGifView(List<Frame> frames) {
        if (frames.isEmpty()) {
            throw new IllegalArgumentException("GIF has no frames");
        }

        double elapsed = 0;
        for (Frame frame : frames) {
            timeline.getKeyFrames().add(new KeyFrame(Duration.millis(elapsed),
                    event -> view.setImage(new Image(new ByteArrayInputStream(frame.data())))));
            elapsed += frame.delayMs();
        }
        timeline.getKeyFrames().add(new KeyFrame(Duration.millis(elapsed)));
        timeline.setCycleCount(Animation.INDEFINITE);

        view.setImage(new Image(new ByteArrayInputStream(frames.get(0).data())));
        view.setPreserveRatio(true);
        view.fitWidthProperty().bind(widthProperty().subtract(20));
        view.fitHeightProperty().bind(heightProperty().subtract(20));
        getChildren().add(view);

        timeline.play();
    }

    public void stop() {
        timeline.stop();
    }

    public record Frame(byte[] data, double delayMs) {
    }

    public static List<Frame> decode(Path file) throws IOException {
        List<Frame> frames = new ArrayList<>();
        Iterator<ImageReader> readers = ImageIO.getImageReadersByFormatName("gif");
        if (!readers.hasNext()) {
            throw new IOException("No GIF reader in ImageIO");
        }
        ImageReader reader = readers.next();
        try (ImageInputStream input = ImageIO.createImageInputStream(file.toFile())) {
            reader.setInput(input);

            int[] screen = logicalScreenSize(reader.getStreamMetadata());
            BufferedImage canvas = new BufferedImage(screen[0], screen[1], BufferedImage.TYPE_INT_ARGB);
            BufferedImage previous = null;

            List<Pending> pending = new ArrayList<>();
            int count = reader.getNumImages(true);
            for (int i = 0; i < count; i++) {
                BufferedImage raw = reader.read(i);
                FrameInfo info = frameInfo(reader.getImageMetadata(i));

                if (info.disposal() == Disposal.RESTORE_PREVIOUS) {
                    previous = copy(canvas);
                }
                Graphics2D g = canvas.createGraphics();
                g.drawImage(raw, info.left(), info.top(), null);
                g.dispose();

                pending.add(new Pending(copy(canvas), Math.max(info.delayMs(), MIN_DELAY_MS)));
                if (pending.size() == BATCH_SIZE) {
                    frames.addAll(encode(pending));
                    pending.clear();
                }

                if (info.disposal() == Disposal.RESTORE_BACKGROUND) {
                    Graphics2D clear = canvas.createGraphics();
                    clear.setComposite(java.awt.AlphaComposite.Clear);
                    clear.fillRect(info.left(), info.top(), raw.getWidth(), raw.getHeight());
                    clear.dispose();
                } else if (info.disposal() == Disposal.RESTORE_PREVIOUS && previous != null) {
                    canvas = copy(previous);
                }
            }
            frames.addAll(encode(pending));
        } finally {
            reader.dispose();
        }
        return frames;
    }

    private record Pending(BufferedImage image, int delayMs) {
    }

    private static List<Frame> encode(List<Pending> pending) throws IOException {
        try {
            return pending.parallelStream()
                    .map(p -> new Frame(toPngUnchecked(p.image()), p.delayMs()))
                    .toList();
        } catch (UncheckedIOException e) {
            throw e.getCause();
        }
    }

    private static byte[] toPngUnchecked(BufferedImage image) {
        try {
            return toPng(image);
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
    }

    private enum Disposal { NONE, RESTORE_BACKGROUND, RESTORE_PREVIOUS }

    private record FrameInfo(int left, int top, int delayMs, Disposal disposal) {
    }

    private static FrameInfo frameInfo(IIOMetadata metadata) {
        int left = 0;
        int top = 0;
        int delayMs = 100;
        Disposal disposal = Disposal.NONE;

        IIOMetadataNode root = (IIOMetadataNode) metadata.getAsTree("javax_imageio_gif_image_1.0");
        NodeList children = root.getChildNodes();
        for (int i = 0; i < children.getLength(); i++) {
            Node child = children.item(i);
            if (child instanceof IIOMetadataNode node) {
                switch (node.getNodeName()) {
                    case "ImageDescriptor" -> {
                        left = Integer.parseInt(node.getAttribute("imageLeftPosition"));
                        top = Integer.parseInt(node.getAttribute("imageTopPosition"));
                    }
                    case "GraphicControlExtension" -> {
                        delayMs = Integer.parseInt(node.getAttribute("delayTime")) * 10;
                        disposal = switch (node.getAttribute("disposalMethod")) {
                            case "restoreToBackgroundColor" -> Disposal.RESTORE_BACKGROUND;
                            case "restoreToPrevious" -> Disposal.RESTORE_PREVIOUS;
                            default -> Disposal.NONE;
                        };
                    }
                    default -> {
                    }
                }
            }
        }
        return new FrameInfo(left, top, delayMs, disposal);
    }

    private static int[] logicalScreenSize(IIOMetadata streamMetadata) {
        IIOMetadataNode root = (IIOMetadataNode) streamMetadata.getAsTree("javax_imageio_gif_stream_1.0");
        NodeList children = root.getElementsByTagName("LogicalScreenDescriptor");
        IIOMetadataNode screen = (IIOMetadataNode) children.item(0);
        return new int[] {
                Integer.parseInt(screen.getAttribute("logicalScreenWidth")),
                Integer.parseInt(screen.getAttribute("logicalScreenHeight"))
        };
    }

    private static BufferedImage copy(BufferedImage source) {
        BufferedImage copy = new BufferedImage(source.getWidth(), source.getHeight(), BufferedImage.TYPE_INT_ARGB);
        Graphics2D g = copy.createGraphics();
        g.drawImage(source, 0, 0, null);
        g.dispose();
        return copy;
    }

    private static byte[] toPng(BufferedImage bufferedImage) throws IOException {
        ByteArrayOutputStream bytes = new ByteArrayOutputStream();
        ImageIO.write(bufferedImage, "png", bytes);
        return bytes.toByteArray();
    }
}
