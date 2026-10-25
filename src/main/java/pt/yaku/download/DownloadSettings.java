package pt.yaku.download;

import java.nio.file.Path;

/** What the user configured for saving files; read fresh each time a download starts. */
public record DownloadSettings(String folder, String template, SidecarFormat sidecar, boolean skipKnown) {

    public boolean hasFolder() {
        return folder != null && !folder.isBlank();
    }

    public Path directory() {
        return Path.of(folder);
    }
}
