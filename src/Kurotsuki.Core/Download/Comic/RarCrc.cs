namespace Kurotsuki.Core.Download.Comic;

/// <summary>CRC-32 (IEEE) as used by RAR 5 and 7z headers.</summary>
internal static class RarCrc
{
    private static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++)
        {
            c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        }
        return c;
    }).ToArray();

    internal static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        return crc;
    }

    internal static uint Crc(byte[] data) => ~Update(0xFFFFFFFFu, data);
}
