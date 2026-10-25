package pt.yaku.download.comic;

import java.util.Arrays;
import java.util.List;
import java.util.Locale;
import java.util.Optional;

import lombok.Getter;

/** Archive types a pool can be saved as. All of them are written by Kurotsuki itself. */
@Getter
public enum ArchiveFormat {
    CBZ("cbz", Kind.ZIP),
    ZIP("zip", Kind.ZIP),
    CBT("cbt", Kind.TAR),
    TAR("tar", Kind.TAR),
    CBR("cbr", Kind.RAR),
    RAR("rar", Kind.RAR),
    CB7("cb7", Kind.SEVEN_ZIP),
    SEVEN_ZIP("7z", Kind.SEVEN_ZIP);

    public enum Kind {
        ZIP, TAR, RAR, SEVEN_ZIP
    }

    public static final ArchiveFormat DEFAULT = CBZ;

    private final String extension;
    private final Kind kind;

    ArchiveFormat(String extension, Kind kind) {
        this.extension = extension;
        this.kind = kind;
    }

    /** Accepts "cbz", ".CBZ", " cbz " and so on. */
    public static Optional<ArchiveFormat> parse(String value) {
        if (value == null) {
            return Optional.empty();
        }
        String cleaned = value.strip().toLowerCase(Locale.ROOT);
        if (cleaned.startsWith(".")) {
            cleaned = cleaned.substring(1);
        }
        String extension = cleaned;
        return Arrays.stream(values()).filter(format -> format.extension.equals(extension)).findFirst();
    }

    public static List<String> extensions() {
        return Arrays.stream(values()).map(ArchiveFormat::getExtension).toList();
    }
}
