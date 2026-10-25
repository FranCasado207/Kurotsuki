package pt.yaku.download.comic;

import java.io.BufferedOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.util.Date;
import java.util.List;
import java.util.function.DoubleConsumer;
import java.util.zip.Deflater;
import java.util.zip.ZipEntry;
import java.util.zip.ZipOutputStream;

import org.apache.commons.compress.archivers.sevenz.SevenZArchiveEntry;
import org.apache.commons.compress.archivers.sevenz.SevenZMethod;
import org.apache.commons.compress.archivers.sevenz.SevenZOutputFile;


/** Writes the pages and ComicInfo.xml into the archive format the user chose. Everything is pure Java. */
public final class ArchiveWriter {

    /** A file to store under {@code name} (a plain file name, no folders). */
    public record Entry(String name, Path file) {
    }

    private static final int TAR_BLOCK = 512;
    private static final int TAR_NAME_LIMIT = 100;

    private ArchiveWriter() {
    }

    /**
     * Writes the archive to {@code target} (replacing it). {@code entries} are stored in order after
     * ComicInfo.xml, which is always the first entry.
     */
    public static void write(ArchiveFormat format, List<Entry> entries, byte[] comicInfo, Path target,
                             DoubleConsumer progress) throws IOException {
        Path partial = target.resolveSibling(target.getFileName() + ".part");
        try {
            switch (format.getKind()) {
                case ZIP -> writeZip(entries, comicInfo, partial, progress);
                case TAR -> writeTar(entries, comicInfo, partial, progress);
                case RAR -> writeRar(entries, comicInfo, partial, progress);
                case SEVEN_ZIP -> writeSevenZip(entries, comicInfo, partial, progress);
            }
            Files.move(partial, target, StandardCopyOption.REPLACE_EXISTING);
        } finally {
            Files.deleteIfExists(partial);
        }
    }

    // ---- zip ------------------------------------------------------------------------------------------------

    private static void writeZip(List<Entry> entries, byte[] comicInfo, Path target, DoubleConsumer progress)
            throws IOException {
        try (ZipOutputStream zip = new ZipOutputStream(new BufferedOutputStream(Files.newOutputStream(target)))) {
            zip.setLevel(Deflater.DEFAULT_COMPRESSION);
            zip.putNextEntry(new ZipEntry(ComicInfo.FILE_NAME));
            zip.write(comicInfo);
            zip.closeEntry();
            // Pages are already compressed images, so deflating them again only costs time.
            zip.setLevel(Deflater.NO_COMPRESSION);
            for (int i = 0; i < entries.size(); i++) {
                zip.putNextEntry(new ZipEntry(entries.get(i).name()));
                Files.copy(entries.get(i).file(), zip);
                zip.closeEntry();
                progress.accept((i + 1.0) / entries.size());
            }
        }
    }

    // ---- tar ------------------------------------------------------------------------------------------------

    private static void writeTar(List<Entry> entries, byte[] comicInfo, Path target, DoubleConsumer progress)
            throws IOException {
        long now = System.currentTimeMillis() / 1000;
        try (OutputStream out = new BufferedOutputStream(Files.newOutputStream(target))) {
            writeTarEntry(out, ComicInfo.FILE_NAME, comicInfo.length, now, new java.io.ByteArrayInputStream(comicInfo));
            for (int i = 0; i < entries.size(); i++) {
                Entry entry = entries.get(i);
                try (InputStream in = Files.newInputStream(entry.file())) {
                    writeTarEntry(out, entry.name(), Files.size(entry.file()), now, in);
                }
                progress.accept((i + 1.0) / entries.size());
            }
            out.write(new byte[2 * TAR_BLOCK]);
        }
    }

    public static void writeTarEntry(OutputStream out, String name, long size, long modifiedSeconds, InputStream data)
            throws IOException {
        byte[] nameBytes = name.getBytes(StandardCharsets.UTF_8);
        if (nameBytes.length > TAR_NAME_LIMIT) {
            throw new IOException("File name too long for a tar archive: " + name);
        }
        byte[] header = new byte[TAR_BLOCK];
        System.arraycopy(nameBytes, 0, header, 0, nameBytes.length);
        putOctal(header, 100, 8, 0644);
        putOctal(header, 108, 8, 0);
        putOctal(header, 116, 8, 0);
        putOctal(header, 124, 12, size);
        putOctal(header, 136, 12, modifiedSeconds);
        java.util.Arrays.fill(header, 148, 156, (byte) ' '); // the checksum is computed with this field blank
        header[156] = '0';
        System.arraycopy("ustar\0".getBytes(StandardCharsets.US_ASCII), 0, header, 257, 6);
        header[263] = '0';
        header[264] = '0';
        long checksum = 0;
        for (byte b : header) {
            checksum += b & 0xFF;
        }
        byte[] sum = String.format("%06o", checksum).getBytes(StandardCharsets.US_ASCII);
        System.arraycopy(sum, 0, header, 148, 6);
        header[154] = 0;
        header[155] = ' ';
        out.write(header);

        long copied = data.transferTo(out);
        if (copied != size) {
            throw new IOException("File changed while archiving: " + name);
        }
        int padding = (int) ((TAR_BLOCK - size % TAR_BLOCK) % TAR_BLOCK);
        out.write(new byte[padding]);
    }

    private static void putOctal(byte[] header, int offset, int length, long value) {
        byte[] digits = String.format("%0" + (length - 1) + "o", value).getBytes(StandardCharsets.US_ASCII);
        System.arraycopy(digits, 0, header, offset, Math.min(digits.length, length - 1));
        header[offset + length - 1] = 0;
    }

    // ---- rar ------------------------------------------------------------------------------------------------

    private static void writeRar(List<Entry> entries, byte[] comicInfo, Path target, DoubleConsumer progress)
            throws IOException {
        try (RarWriter rar = new RarWriter(new BufferedOutputStream(Files.newOutputStream(target)))) {
            rar.add(ComicInfo.FILE_NAME, comicInfo);
            for (int i = 0; i < entries.size(); i++) {
                rar.add(entries.get(i).name(), entries.get(i).file());
                progress.accept((i + 1.0) / entries.size());
            }
        }
    }

    // ---- 7z -------------------------------------------------------------------------------------------------

    private static void writeSevenZip(List<Entry> entries, byte[] comicInfo, Path target, DoubleConsumer progress)
            throws IOException {
        try (SevenZOutputFile archive = new SevenZOutputFile(target.toFile())) {
            // Stored, not LZMA: the pages are compressed images, and this needs no extra compression library.
            archive.setContentCompression(SevenZMethod.COPY);
            SevenZArchiveEntry info = new SevenZArchiveEntry();
            info.setName(ComicInfo.FILE_NAME);
            info.setSize(comicInfo.length);
            info.setLastModifiedDate(new Date());
            archive.putArchiveEntry(info);
            archive.write(comicInfo);
            archive.closeArchiveEntry();
            byte[] buffer = new byte[64 * 1024];
            for (int i = 0; i < entries.size(); i++) {
                SevenZArchiveEntry entry = archive.createArchiveEntry(entries.get(i).file().toFile(),
                        entries.get(i).name());
                archive.putArchiveEntry(entry);
                try (InputStream in = Files.newInputStream(entries.get(i).file())) {
                    int read;
                    while ((read = in.read(buffer)) != -1) {
                        archive.write(buffer, 0, read);
                    }
                }
                archive.closeArchiveEntry();
                progress.accept((i + 1.0) / entries.size());
            }
            archive.finish();
        }
    }
}
