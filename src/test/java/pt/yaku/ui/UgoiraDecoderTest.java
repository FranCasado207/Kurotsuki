package pt.yaku.ui;

import java.io.IOException;
import java.io.OutputStream;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;
import java.util.zip.ZipEntry;
import java.util.zip.ZipOutputStream;

import static org.junit.jupiter.api.Assertions.*;

import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

public class UgoiraDecoderTest {

    private static Path zip(String... nameAndSizePairs) throws IOException {
        Path file = Files.createTempFile("ugoira", ".zip");
        try (OutputStream out = Files.newOutputStream(file); ZipOutputStream zip = new ZipOutputStream(out)) {
            for (int i = 0; i < nameAndSizePairs.length; i += 2) {
                zip.putNextEntry(new ZipEntry(nameAndSizePairs[i]));
                zip.write(new byte[Integer.parseInt(nameAndSizePairs[i + 1])]);
                zip.closeEntry();
            }
        }
        return file;
    }

    @Test
    public void testDecodesFramesInNameOrder() throws IOException {
        Path file = zip("000001.jpg", "10", "000000.jpg", "20");
        try {
            List<AnimatedGifView.Frame> frames = UgoiraDecoder.decode(file);
            assertEquals(2, frames.size());
            assertEquals(20, frames.get(0).data().length);
        } finally {
            Files.deleteIfExists(file);
        }
    }

    @Test
    public void testOversizedEntryIsRejected() throws IOException {
        Path file = zip("0.jpg", String.valueOf(UgoiraDecoder.MAX_FRAME_BYTES + 1));
        try {
            UgoiraDecoder.decode(file);
            fail("expected IOException");
        } catch (IOException expected) {
            // ok
        } finally {
            Files.deleteIfExists(file);
        }
    }

    @Test
    public void testTooManyFramesAreRejected() throws IOException {
        String[] entries = new String[(UgoiraDecoder.MAX_FRAMES + 1) * 2];
        for (int i = 0; i <= UgoiraDecoder.MAX_FRAMES; i++) {
            entries[i * 2] = String.format("%05d.png", i);
            entries[i * 2 + 1] = "1";
        }
        Path file = zip(entries);
        try {
            UgoiraDecoder.decode(file);
            fail("expected IOException");
        } catch (IOException expected) {
            // ok
        } finally {
            Files.deleteIfExists(file);
        }
    }

    @Test
    public void testHugeDimensionsAreRejectedBeforeDecoding() throws IOException {
        int side = (int) Math.sqrt(UgoiraDecoder.MAX_FRAME_PIXELS) + 100;
        java.awt.image.BufferedImage image = new java.awt.image.BufferedImage(side, side,
                java.awt.image.BufferedImage.TYPE_BYTE_BINARY);
        java.io.ByteArrayOutputStream png = new java.io.ByteArrayOutputStream();
        javax.imageio.ImageIO.write(image, "png", png);
        try {
            UgoiraDecoder.checkDimensions(png.toByteArray(), "big.png");
            fail("expected IOException");
        } catch (IOException expected) {
            // ok
        }
        UgoiraDecoder.checkDimensions(new byte[10], "not-an-image.jpg");
    }
}
