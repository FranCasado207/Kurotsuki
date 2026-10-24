package pt.yaku.ui;

import java.awt.image.BufferedImage;
import java.io.ByteArrayInputStream;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;
import java.util.Locale;

import javax.imageio.IIOImage;
import javax.imageio.ImageIO;
import javax.imageio.ImageTypeSpecifier;
import javax.imageio.ImageWriteParam;
import javax.imageio.ImageWriter;
import javax.imageio.metadata.IIOMetadata;
import javax.imageio.metadata.IIOMetadataNode;
import javax.imageio.stream.ImageOutputStream;

public final class UgoiraGif {

    private static final String GIF_METADATA = "javax_imageio_gif_image_1.0";

    private UgoiraGif() {
    }

    public static boolean isUgoiraZip(Path file) {
        return file.getFileName().toString().toLowerCase(Locale.ROOT).endsWith(".zip");
    }

    public static Path gifNameFor(Path zip) {
        String name = zip.getFileName().toString();
        String base = name.substring(0, name.length() - ".zip".length());
        return zip.resolveSibling(base + ".gif");
    }

    public static Path convert(Path zip, Path gif) throws IOException {
        List<AnimatedGifView.Frame> frames = UgoiraDecoder.decode(zip);
        Path parent = gif.toAbsolutePath().getParent();
        if (parent != null) {
            Files.createDirectories(parent);
        }
        write(frames, gif);
        return gif;
    }

    private static void write(List<AnimatedGifView.Frame> frames, Path target) throws IOException {
        ImageWriter writer = ImageIO.getImageWritersByFormatName("gif").next();
        try (ImageOutputStream out = ImageIO.createImageOutputStream(target.toFile())) {
            writer.setOutput(out);
            writer.prepareWriteSequence(null);
            for (int i = 0; i < frames.size(); i++) {
                BufferedImage image = ImageIO.read(new ByteArrayInputStream(frames.get(i).data()));
                if (image == null) {
                    throw new IOException("Unreadable ugoira frame " + (i + 1));
                }
                ImageWriteParam param = writer.getDefaultWriteParam();
                IIOMetadata metadata = writer.getDefaultImageMetadata(new ImageTypeSpecifier(image), param);
                configure(metadata, frames.get(i).delayMs(), i == 0);
                writer.writeToSequence(new IIOImage(image, null, metadata), param);
            }
            writer.endWriteSequence();
        } finally {
            writer.dispose();
        }
    }

    private static void configure(IIOMetadata metadata, double delayMs, boolean first) throws IOException {
        IIOMetadataNode root = (IIOMetadataNode) metadata.getAsTree(GIF_METADATA);
        IIOMetadataNode control = node(root, "GraphicControlExtension");
        control.setAttribute("disposalMethod", "none");
        control.setAttribute("userInputFlag", "FALSE");
        control.setAttribute("transparentColorFlag", "FALSE");
        control.setAttribute("delayTime", String.valueOf(Math.max(2, Math.round(delayMs / 10.0))));
        control.setAttribute("transparentColorIndex", "0");
        if (first) {
            IIOMetadataNode loop = new IIOMetadataNode("ApplicationExtensions");
            IIOMetadataNode app = new IIOMetadataNode("ApplicationExtension");
            app.setAttribute("applicationID", "NETSCAPE");
            app.setAttribute("authenticationCode", "2.0");
            app.setUserObject(new byte[] {1, 0, 0});
            loop.appendChild(app);
            root.appendChild(loop);
        }
        metadata.setFromTree(GIF_METADATA, root);
    }

    private static IIOMetadataNode node(IIOMetadataNode root, String name) {
        for (int i = 0; i < root.getLength(); i++) {
            if (root.item(i).getNodeName().equals(name)) {
                return (IIOMetadataNode) root.item(i);
            }
        }
        IIOMetadataNode node = new IIOMetadataNode(name);
        root.appendChild(node);
        return node;
    }
}
