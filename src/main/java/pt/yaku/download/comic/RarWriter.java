package pt.yaku.download.comic;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.zip.CRC32;

/**
 * Writes RAR 5 archives that store their files uncompressed. That is all a comic needs (pages are already
 * compressed images), and it needs no RAR compressor: the container layout is documented by RARLAB and every
 * RAR reader, including comic readers, opens such archives.
 */
final class RarWriter implements AutoCloseable {

    private static final byte[] SIGNATURE = {0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00};
    private static final int HEADER_MAIN = 1;
    private static final int HEADER_FILE = 2;
    private static final int HEADER_END = 5;
    private static final int FLAG_DATA_AREA = 0x2;
    private static final int FILE_FLAG_TIME = 0x2;
    private static final int FILE_FLAG_CRC = 0x4;
    private static final int HOST_OS_UNIX = 1;
    private static final int UNIX_MODE_FILE = 0644;

    private final OutputStream out;
    private final long modifiedSeconds = System.currentTimeMillis() / 1000;

    RarWriter(OutputStream out) throws IOException {
        this.out = out;
        out.write(SIGNATURE);
        writeHeader(vint(HEADER_MAIN), vint(0), vint(0)); // type, header flags, archive flags
    }

    void add(String name, byte[] data) throws IOException {
        CRC32 crc = new CRC32();
        crc.update(data);
        writeFileHeader(name, data.length, crc.getValue());
        out.write(data);
    }

    void add(String name, Path file) throws IOException {
        long size = Files.size(file);
        CRC32 crc = new CRC32();
        try (InputStream in = Files.newInputStream(file)) {
            byte[] buffer = new byte[64 * 1024];
            int read;
            while ((read = in.read(buffer)) != -1) {
                crc.update(buffer, 0, read);
            }
        }
        writeFileHeader(name, size, crc.getValue());
        try (InputStream in = Files.newInputStream(file)) {
            if (in.transferTo(out) != size) {
                throw new IOException("File changed while archiving: " + name);
            }
        }
    }

    private void writeFileHeader(String name, long size, long crc) throws IOException {
        byte[] nameBytes = name.getBytes(StandardCharsets.UTF_8);
        writeHeader(vint(HEADER_FILE), vint(FLAG_DATA_AREA), vint(size), // data area = the stored file
                vint(FILE_FLAG_TIME | FILE_FLAG_CRC), vint(size), vint(UNIX_MODE_FILE),
                uint32(modifiedSeconds), uint32(crc),
                vint(0), // compression: version 0, method 0 = store
                vint(HOST_OS_UNIX), vint(nameBytes.length), nameBytes);
    }

    @Override
    public void close() throws IOException {
        writeHeader(vint(HEADER_END), vint(0), vint(0)); // type, header flags, end-of-archive flags
        out.flush();
    }

    /** Every header is: CRC32 of the rest, then the size of the body as a vint, then the body. */
    private void writeHeader(byte[]... fields) throws IOException {
        ByteArrayOutputStream body = new ByteArrayOutputStream();
        for (byte[] field : fields) {
            body.write(field);
        }
        byte[] sized = concat(vint(body.size()), body.toByteArray());
        CRC32 crc = new CRC32();
        crc.update(sized);
        out.write(uint32(crc.getValue()));
        out.write(sized);
    }

    /** Variable length integer: 7 bits per byte, least significant first, high bit set when more follow. */
    static byte[] vint(long value) {
        ByteArrayOutputStream bytes = new ByteArrayOutputStream(10);
        long rest = value;
        while (rest >= 0x80) {
            bytes.write((int) (rest & 0x7F) | 0x80);
            rest >>>= 7;
        }
        bytes.write((int) rest);
        return bytes.toByteArray();
    }

    private static byte[] uint32(long value) {
        return ByteBuffer.allocate(4).order(ByteOrder.LITTLE_ENDIAN).putInt((int) value).array();
    }

    private static byte[] concat(byte[] first, byte[] second) {
        byte[] joined = new byte[first.length + second.length];
        System.arraycopy(first, 0, joined, 0, first.length);
        System.arraycopy(second, 0, joined, first.length, second.length);
        return joined;
    }
}
