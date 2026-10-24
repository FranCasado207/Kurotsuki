package pt.yaku.ui;

import java.io.ByteArrayInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Iterator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.zip.ZipEntry;
import java.util.zip.ZipFile;

import javax.imageio.ImageIO;
import javax.imageio.ImageReader;
import javax.imageio.stream.ImageInputStream;

import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

public final class UgoiraDecoder {

    private static final int DEFAULT_DELAY_MS = 100;
    static final int MAX_FRAMES = 2000;
    static final long MAX_FRAME_BYTES = 32L << 20;
    static final long MAX_TOTAL_BYTES = 256L << 20;
    static final long MAX_FRAME_PIXELS = 64_000_000L;
    private static final int MAX_METADATA_BYTES = 1 << 20;

    private UgoiraDecoder() {
    }

    public static List<AnimatedGifView.Frame> decode(Path zipFile) throws IOException {
        try (ZipFile zip = new ZipFile(zipFile.toFile())) {
            Map<String, Integer> order = frameOrder(zip);
            if (order.size() > MAX_FRAMES) {
                throw new IOException("Ugoira has too many frames (" + order.size() + ")");
            }
            List<AnimatedGifView.Frame> frames = new ArrayList<>();
            long total = 0;
            for (Map.Entry<String, Integer> frame : order.entrySet()) {
                ZipEntry entry = zip.getEntry(frame.getKey());
                if (entry == null || entry.isDirectory()) {
                    continue;
                }
                // Entry sizes in the header can lie, so the limit is enforced while reading.
                byte[] data = readBounded(zip, entry, MAX_FRAME_BYTES);
                checkDimensions(data, entry.getName());
                total += data.length;
                if (total > MAX_TOTAL_BYTES) {
                    throw new IOException("Ugoira is too large when unpacked");
                }
                frames.add(new AnimatedGifView.Frame(data, frame.getValue()));
            }
            if (frames.isEmpty()) {
                throw new IOException("ZIP without image frames");
            }
            return frames;
        }
    }

    /** Reads only the image header, so a tiny file that claims huge dimensions is refused before decoding. */
    static void checkDimensions(byte[] data, String name) throws IOException {
        try (ImageInputStream input = ImageIO.createImageInputStream(new ByteArrayInputStream(data))) {
            Iterator<ImageReader> readers = input == null ? Collections.emptyIterator() : ImageIO.getImageReaders(input);
            if (!readers.hasNext()) {
                return; // not a format ImageIO knows; the frame is rejected later if it cannot be decoded
            }
            ImageReader reader = readers.next();
            try {
                reader.setInput(input, true, true);
                long pixels = (long) reader.getWidth(0) * reader.getHeight(0);
                if (pixels > MAX_FRAME_PIXELS) {
                    throw new IOException("Ugoira frame has too many pixels: " + name);
                }
            } finally {
                reader.dispose();
            }
        }
    }

    private static byte[] readBounded(ZipFile zip, ZipEntry entry, long limit) throws IOException {
        try (InputStream in = zip.getInputStream(entry)) {
            byte[] data = in.readNBytes((int) limit + 1);
            if (data.length > limit) {
                throw new IOException("Ugoira entry too large: " + entry.getName());
            }
            return data;
        }
    }

    private static Map<String, Integer> frameOrder(ZipFile zip) throws IOException {
        Map<String, Integer> order = new LinkedHashMap<>();
        ZipEntry metadata = zip.getEntry("animation.json");
        if (metadata != null) {
            String text = new String(readBounded(zip, metadata, MAX_METADATA_BYTES), StandardCharsets.UTF_8);
            try {
                JsonObject json = JsonParser.parseString(text).getAsJsonObject();
                for (JsonElement element : json.getAsJsonArray("frames")) {
                    JsonObject frame = element.getAsJsonObject();
                    order.put(frame.get("file").getAsString(), frame.get("delay").getAsInt());
                }
            } catch (RuntimeException e) {
                order.clear();
            }
        }
        if (order.isEmpty()) {
            zip.stream()
                    .filter(entry -> !entry.isDirectory() && isImage(entry.getName()))
                    .map(ZipEntry::getName)
                    .sorted()
                    .forEach(name -> order.put(name, DEFAULT_DELAY_MS));
        }
        return order;
    }

    private static boolean isImage(String name) {
        String lower = name.toLowerCase(Locale.ROOT);
        return lower.endsWith(".jpg") || lower.endsWith(".jpeg") || lower.endsWith(".png")
                || lower.endsWith(".gif") || lower.endsWith(".webp");
    }
}
