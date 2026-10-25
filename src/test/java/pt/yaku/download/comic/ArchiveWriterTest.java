package pt.yaku.download.comic;

import static org.junit.jupiter.api.Assertions.*;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.stream.Stream;
import java.util.zip.CRC32;
import java.util.zip.ZipEntry;
import java.util.zip.ZipFile;

import org.apache.commons.compress.archivers.sevenz.SevenZArchiveEntry;
import org.apache.commons.compress.archivers.sevenz.SevenZFile;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

public class ArchiveWriterTest {

    private static final byte[] INFO = "<ComicInfo/>".getBytes(StandardCharsets.UTF_8);

    private Path dir;
    private List<ArchiveWriter.Entry> entries;

    @BeforeEach
    void setUp() throws IOException {
        dir = Files.createTempDirectory("kurotsuki-archive-test");
        entries = new ArrayList<>();
        for (int i = 1; i <= 3; i++) {
            Path file = dir.resolve("src" + i);
            Files.write(file, ("page-" + i + "-").repeat(300 * i).getBytes(StandardCharsets.UTF_8));
            entries.add(new ArchiveWriter.Entry("00" + i + ".png", file));
        }
    }

    @AfterEach
    void tearDown() throws IOException {
        try (Stream<Path> files = Files.walk(dir)) {
            files.sorted(Comparator.reverseOrder()).forEach(p -> p.toFile().delete());
        }
    }

    private static boolean hasTool(String name) {
        return findOnPath(name) != null;
    }

    private static String findOnPath(String name) {
        for (String directory : System.getenv("PATH").split(java.io.File.pathSeparator)) {
            Path candidate = Path.of(directory).resolve(name);
            if (Files.isExecutable(candidate)) {
                return candidate.toString();
            }
        }
        return null;
    }

    private static String firstOf(String... names) {
        for (String name : names) {
            String found = findOnPath(name);
            if (found != null) {
                return found;
            }
        }
        return null;
    }

    private static String run(Path directory, String... command) throws Exception {
        Process process = new ProcessBuilder(command).directory(directory.toFile()).redirectErrorStream(true).start();
        String output = new String(process.getInputStream().readAllBytes(), StandardCharsets.UTF_8);
        assertEquals(0, process.waitFor(), output);
        return output;
    }

    @Test
    public void testFormatsAreParsedLeniently() {
        assertEquals(ArchiveFormat.CBZ, ArchiveFormat.parse(" .CBZ ").orElseThrow());
        assertEquals(ArchiveFormat.SEVEN_ZIP, ArchiveFormat.parse("7z").orElseThrow());
        assertTrue(ArchiveFormat.parse("exe").isEmpty());
        assertTrue(ArchiveFormat.parse(null).isEmpty());
        assertEquals(List.of("cbz", "zip", "cbt", "tar", "cbr", "rar", "cb7", "7z"), ArchiveFormat.extensions());
    }

    @Test
    public void testZipHoldsComicInfoFirstThenThePagesInOrder() throws Exception {
        Path target = dir.resolve("out.cbz");
        ArchiveWriter.write(ArchiveFormat.CBZ, entries, INFO, target, p -> { });
        try (ZipFile zip = new ZipFile(target.toFile())) {
            List<String> names = zip.stream().map(ZipEntry::getName).toList();
            assertEquals(List.of("ComicInfo.xml", "001.png", "002.png", "003.png"), names);
            assertArrayEquals(INFO, zip.getInputStream(zip.getEntry("ComicInfo.xml")).readAllBytes());
            assertArrayEquals(Files.readAllBytes(entries.get(2).file()),
                    zip.getInputStream(zip.getEntry("003.png")).readAllBytes());
        }
        assertFalse(Files.exists(dir.resolve("out.cbz.part")));
    }

    @Test
    public void testTarIsReadableByTheSystemTar() throws Exception {
        assumeTrue(hasTool("tar"));
        Path target = dir.resolve("out.cbt");
        ArchiveWriter.write(ArchiveFormat.CBT, entries, INFO, target, p -> { });
        assertEquals(List.of("ComicInfo.xml", "001.png", "002.png", "003.png"),
                List.of(run(dir, "tar", "-tf", target.toString()).split("\\R")));
        Path extracted = Files.createDirectory(dir.resolve("extracted"));
        run(dir, "tar", "-xf", target.toString(), "-C", extracted.toString());
        assertArrayEquals(Files.readAllBytes(entries.get(1).file()), Files.readAllBytes(extracted.resolve("002.png")));
        assertArrayEquals(INFO, Files.readAllBytes(extracted.resolve("ComicInfo.xml")));
    }

    @Test
    public void testSevenZipNeedsNoExternalProgram() throws Exception {
        Path target = dir.resolve("out.cb7");
        ArchiveWriter.write(ArchiveFormat.CB7, entries, INFO, target, p -> { });
        try (SevenZFile archive = SevenZFile.builder().setFile(target.toFile()).get()) {
            List<String> names = new ArrayList<>();
            SevenZArchiveEntry entry;
            while ((entry = archive.getNextEntry()) != null) {
                names.add(entry.getName());
                byte[] content = new byte[(int) entry.getSize()];
                archive.read(content);
                if (entry.getName().equals("002.png")) {
                    assertArrayEquals(Files.readAllBytes(entries.get(1).file()), content);
                }
            }
            assertEquals(List.of("ComicInfo.xml", "001.png", "002.png", "003.png"), names);
        }
        assertFalse(Files.exists(dir.resolve("out.cb7.part")));
    }

    @Test
    public void testSevenZipIsAcceptedByTheOfficialTool() throws Exception {
        String tool = firstOf("7zz", "7z", "7za");
        assumeTrue(tool != null);
        Path target = dir.resolve("official.cb7");
        ArchiveWriter.write(ArchiveFormat.CB7, entries, INFO, target, p -> { });
        String listing = run(dir, tool, "t", target.toString());
        assertTrue(listing.contains("Everything is Ok"), listing);
    }

    @Test
    public void testRarArchiveHasValidStructure() throws Exception {
        Path target = dir.resolve("out.cbr");
        ArchiveWriter.write(ArchiveFormat.CBR, entries, INFO, target, p -> { });
        assertFalse(Files.exists(dir.resolve("out.cbr.part")));

        // Walk the archive by its own headers: signature, CRC of every header, CRC of every file.
        ByteBuffer file = ByteBuffer.wrap(Files.readAllBytes(target)).order(ByteOrder.LITTLE_ENDIAN);
        byte[] signature = new byte[8];
        file.get(signature);
        assertArrayEquals(new byte[] {0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00}, signature);
        List<String> names = new ArrayList<>();
        boolean ended = false;
        while (file.hasRemaining() && !ended) {
            long storedCrc = file.getInt() & 0xFFFFFFFFL;
            int sizeStart = file.position();
            long headerSize = vint(file);
            int bodyStart = file.position();
            CRC32 crc = new CRC32();
            crc.update(file.array(), sizeStart, bodyStart - sizeStart + (int) headerSize);
            assertEquals(crc.getValue(), storedCrc, "header CRC");
            long type = vint(file);
            long flags = vint(file);
            if (type == 5) {
                ended = true;
            } else if (type == 2) {
                long dataSize = vint(file);
                long fileFlags = vint(file);
                long unpacked = vint(file);
                vint(file); // attributes
                file.getInt(); // modification time
                long dataCrc = file.getInt() & 0xFFFFFFFFL;
                assertEquals(0, vint(file), "stored, not compressed");
                vint(file); // host OS
                byte[] name = new byte[(int) vint(file)];
                file.get(name);
                assertEquals(6, fileFlags);
                assertEquals(unpacked, dataSize);
                file.position(bodyStart + (int) headerSize);
                CRC32 data = new CRC32();
                data.update(file.array(), file.position(), (int) dataSize);
                assertEquals(data.getValue(), dataCrc, "file CRC");
                file.position(file.position() + (int) dataSize);
                names.add(new String(name, StandardCharsets.UTF_8));
                continue;
            }
            file.position(bodyStart + (int) headerSize);
        }
        assertTrue(ended);
        assertEquals(List.of("ComicInfo.xml", "001.png", "002.png", "003.png"), names);
    }

    @Test
    public void testRarArchiveIsAcceptedByOfficialReaders() throws Exception {
        Path target = dir.resolve("official.cbr");
        ArchiveWriter.write(ArchiveFormat.CBR, entries, INFO, target, p -> { });
        boolean checked = false;
        String unrar = findOnPath("unrar");
        if (unrar != null) {
            assertTrue(run(dir, unrar, "t", target.toString()).contains("All OK"));
            checked = true;
        }
        String sevenZip = firstOf("7zz", "7z", "7za");
        if (sevenZip != null) {
            assertTrue(run(dir, sevenZip, "t", target.toString()).contains("Everything is Ok"));
            checked = true;
        }
        assumeTrue(checked, "neither unrar nor 7z is installed here");
    }

    @Test
    public void testVintEncoding() {
        assertArrayEquals(new byte[] {0}, RarWriter.vint(0));
        assertArrayEquals(new byte[] {0x7F}, RarWriter.vint(127));
        assertArrayEquals(new byte[] {(byte) 0x80, 0x01}, RarWriter.vint(128));
        assertArrayEquals(new byte[] {(byte) 0xAC, 0x02}, RarWriter.vint(300));
    }

    private static long vint(ByteBuffer buffer) {
        long value = 0;
        int shift = 0;
        while (true) {
            int b = buffer.get() & 0xFF;
            value |= (long) (b & 0x7F) << shift;
            if ((b & 0x80) == 0) {
                return value;
            }
            shift += 7;
        }
    }
}
