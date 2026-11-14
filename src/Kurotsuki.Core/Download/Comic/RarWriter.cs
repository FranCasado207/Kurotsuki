using System.Buffers.Binary;
using System.Text;

namespace Kurotsuki.Core.Download.Comic;

/// <summary>
/// Writes RAR 5 archives that store their files uncompressed. That is all a comic needs (pages are already
/// compressed images) and it needs no RAR compressor: the container layout is documented by RARLAB and every RAR
/// reader, comic readers included, opens such archives. Port of the Java RarWriter.
/// </summary>
internal sealed class RarWriter : IDisposable
{
    private static readonly byte[] Signature = [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00];
    private const int HeaderMain = 1, HeaderFile = 2, HeaderEnd = 5;
    private const int FlagDataArea = 0x2, FileFlagTime = 0x2, FileFlagCrc = 0x4, HostOsUnix = 1, UnixModeFile = 0x1A4; // 0644

    private readonly Stream _out;
    private readonly uint _modifiedSeconds = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public RarWriter(Stream output)
    {
        _out = output;
        _out.Write(Signature);
        WriteHeader(VInt(HeaderMain), VInt(0), VInt(0)); // type, header flags, archive flags
    }

    public void Add(string name, byte[] data)
    {
        WriteFileHeader(name, data.Length, RarCrc.Crc(data));
        _out.Write(data);
    }

    public void Add(string name, string file)
    {
        var size = new FileInfo(file).Length;
        var crc = 0xFFFFFFFFu;
        using (var input = File.OpenRead(file))
        {
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = input.Read(buffer)) > 0)
            {
                crc = RarCrc.Update(crc, buffer.AsSpan(0, read));
            }
        }
        WriteFileHeader(name, size, ~crc);
        using var again = File.OpenRead(file);
        again.CopyTo(_out);
    }

    private void WriteFileHeader(string name, long size, uint crc)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name);
        WriteHeader(VInt(HeaderFile), VInt(FlagDataArea), VInt(size), // data area = the stored file
            VInt(FileFlagTime | FileFlagCrc), VInt(size), VInt(UnixModeFile), UInt32(_modifiedSeconds), UInt32(crc),
            VInt(0), // compression: version 0, method 0 = store
            VInt(HostOsUnix), VInt(nameBytes.Length), nameBytes);
    }

    public void Dispose()
    {
        WriteHeader(VInt(HeaderEnd), VInt(0), VInt(0)); // type, header flags, end-of-archive flags
        _out.Flush();
    }

    /// <summary>Every header is: CRC32 of the rest, then the size of the body as a vint, then the body.</summary>
    private void WriteHeader(params byte[][] fields)
    {
        var body = fields.SelectMany(f => f).ToArray();
        var sized = VInt(body.Length).Concat(body).ToArray();
        _out.Write(UInt32(RarCrc.Crc(sized)));
        _out.Write(sized);
    }

    /// <summary>Variable length integer: 7 bits per byte, least significant first, high bit set when more follow.</summary>
    internal static byte[] VInt(long value)
    {
        var bytes = new List<byte>(10);
        var rest = (ulong)value;
        while (rest >= 0x80)
        {
            bytes.Add((byte)((rest & 0x7F) | 0x80));
            rest >>= 7;
        }
        bytes.Add((byte)rest);
        return [.. bytes];
    }

    private static byte[] UInt32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }
}
