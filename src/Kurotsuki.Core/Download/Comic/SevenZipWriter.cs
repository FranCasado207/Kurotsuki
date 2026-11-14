using System.Buffers.Binary;
using System.Text;

namespace Kurotsuki.Core.Download.Comic;

/// <summary>
/// Writes 7z archives that store their files uncompressed (the "Copy" method). Comic pages are already compressed
/// images, so nothing is lost, and the container needs no LZMA encoder; 7-Zip, p7zip, unarchivers and comic readers
/// open such archives. One folder per file keeps the header simple. Layout per the 7z format description (7zFormat.txt).
/// </summary>
internal sealed class SevenZipWriter
{
    private sealed record Item(string Name, long Size, uint Crc);

    private readonly Stream _out;
    private readonly List<Item> _items = [];
    private readonly byte[] _signature = [(byte)'7', (byte)'z', 0xBC, 0xAF, 0x27, 0x1C, 0, 4];

    public SevenZipWriter(Stream output)
    {
        _out = output;
        _out.Write(new byte[32]); // start header, filled in by Finish()
    }

    public void Add(string name, byte[] data) => Add(name, new MemoryStream(data), data.Length);

    public void Add(string name, string file)
    {
        using var input = File.OpenRead(file);
        Add(name, input, input.Length);
    }

    private void Add(string name, Stream data, long size)
    {
        var crc = 0xFFFFFFFFu;
        var buffer = new byte[64 * 1024];
        long copied = 0;
        int read;
        while ((read = data.Read(buffer)) > 0)
        {
            crc = RarCrc.Update(crc, buffer.AsSpan(0, read));
            _out.Write(buffer, 0, read);
            copied += read;
        }
        if (copied != size)
        {
            throw new IOException($"File changed while archiving: {name}");
        }
        _items.Add(new Item(name, size, ~crc));
    }

    /// <summary>Writes the header and fixes up the start header. The stream must be seekable.</summary>
    public void Finish()
    {
        var packEnd = _out.Position;
        var header = BuildHeader();
        _out.Write(header);

        var next = new byte[20];
        BinaryPrimitives.WriteInt64LittleEndian(next, packEnd - 32);
        BinaryPrimitives.WriteInt64LittleEndian(next.AsSpan(8), header.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(next.AsSpan(16), RarCrc.Crc(header));

        var start = new byte[32];
        _signature.CopyTo(start, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(start.AsSpan(8), RarCrc.Crc(next));
        next.CopyTo(start, 12);
        _out.Position = 0;
        _out.Write(start);
        _out.Seek(0, SeekOrigin.End);
        _out.Flush();
    }

    private byte[] BuildHeader()
    {
        using var h = new MemoryStream();
        void Byte(int b) => h.WriteByte((byte)b);
        void Number(long n) => h.Write(EncodeNumber(n));

        Byte(0x01); // Header
        if (_items.Count > 0)
        {
            Byte(0x04); // MainStreamsInfo
            Byte(0x06); // PackInfo
            Number(0); // pack position
            Number(_items.Count);
            Byte(0x09); // sizes
            foreach (var item in _items)
            {
                Number(item.Size);
            }
            Byte(0x00);

            Byte(0x07); // UnpackInfo
            Byte(0x0B); // Folder
            Number(_items.Count);
            Byte(0); // not external
            foreach (var _ in _items)
            {
                Number(1); // one coder
                Byte(0x01); // simple coder, id size 1
                Byte(0x00); // Copy
            }
            Byte(0x0C); // coder unpack sizes
            foreach (var item in _items)
            {
                Number(item.Size);
            }
            Byte(0x00);

            Byte(0x08); // SubStreamsInfo (one stream per folder, so only the CRCs are listed)
            Byte(0x0A);
            Byte(1); // all defined
            foreach (var item in _items)
            {
                var crc = new byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(crc, item.Crc);
                h.Write(crc);
            }
            Byte(0x00);
            Byte(0x00); // end of MainStreamsInfo

            Byte(0x05); // FilesInfo
            Number(_items.Count);
            var names = new MemoryStream();
            names.WriteByte(0); // not external
            foreach (var item in _items)
            {
                names.Write(Encoding.Unicode.GetBytes(item.Name.Replace('\\', '/')));
                names.Write(new byte[2]);
            }
            Byte(0x11); // Names
            Number(names.Length);
            h.Write(names.ToArray());
            Byte(0x00);
        }
        Byte(0x00); // end of header
        return h.ToArray();
    }

    /// <summary>7z variable-length number: the leading 1-bits of the first byte say how many extra bytes follow.</summary>
    internal static byte[] EncodeNumber(long value)
    {
        var v = (ulong)value;
        for (var extra = 0; extra < 8; extra++)
        {
            if (v < 1UL << (7 * (extra + 1)))
            {
                var bytes = new byte[extra + 1];
                for (var i = 0; i < extra; i++)
                {
                    bytes[i + 1] = (byte)(v >> (8 * i));
                }
                var mask = (byte)(0xFF00 >> extra);
                bytes[0] = (byte)(mask | (byte)(v >> (8 * extra)));
                return bytes;
            }
        }
        var all = new byte[9];
        all[0] = 0xFF;
        BinaryPrimitives.WriteUInt64LittleEndian(all.AsSpan(1), v);
        return all;
    }
}
