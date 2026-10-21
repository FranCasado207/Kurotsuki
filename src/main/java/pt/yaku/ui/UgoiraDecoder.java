package pt.yaku.ui;

import java.io.IOException;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.zip.ZipEntry;
import java.util.zip.ZipFile;

import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

public final class UgoiraDecoder {

    private static final int DEFAULT_DELAY_MS = 100;

    private UgoiraDecoder() {
    }

    public static List<AnimatedGifView.Frame> decode(Path zipFile) throws IOException {
        try (ZipFile zip = new ZipFile(zipFile.toFile())) {
            Map<String, Integer> order = frameOrder(zip);
            List<AnimatedGifView.Frame> frames = new ArrayList<>();
            for (Map.Entry<String, Integer> frame : order.entrySet()) {
                ZipEntry entry = zip.getEntry(frame.getKey());
                if (entry == null) {
                    continue;
                }
                byte[] data = zip.getInputStream(entry).readAllBytes();
                frames.add(new AnimatedGifView.Frame(data, frame.getValue()));
            }
            if (frames.isEmpty()) {
                throw new IOException("ZIP without image frames");
            }
            return frames;
        }
    }

    private static Map<String, Integer> frameOrder(ZipFile zip) throws IOException {
        Map<String, Integer> order = new LinkedHashMap<>();
        ZipEntry metadata = zip.getEntry("animation.json");
        if (metadata != null) {
            JsonObject json = JsonParser.parseString(new String(zip.getInputStream(metadata).readAllBytes()))
                    .getAsJsonObject();
            for (JsonElement element : json.getAsJsonArray("frames")) {
                JsonObject frame = element.getAsJsonObject();
                order.put(frame.get("file").getAsString(), frame.get("delay").getAsInt());
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
