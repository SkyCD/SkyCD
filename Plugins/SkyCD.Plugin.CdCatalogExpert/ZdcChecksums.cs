namespace SkyCD.Plugin.CdCatalogExpert;

/// <summary>
/// Integrity algorithms shared by the ZDC reader and writer.
/// </summary>
public static class ZdcChecksums
{
    private static readonly ushort[] Crc16Table = BuildCrc16Table();

    /// <summary>
    /// Chunk XOR checksum: splits the covered bytes into little-endian UInt32
    /// words and XORs every word starting from zero.
    /// </summary>
    public static uint XorWords(byte[] data, int offset, int count)
    {
        var checksum = 0u;
        for (var i = 0; i < count; i += 4)
        {
            checksum ^= (uint)(data[offset + i]
                | (data[offset + i + 1] << 8)
                | (data[offset + i + 2] << 16)
                | (data[offset + i + 3] << 24));
        }

        return checksum;
    }

    /// <summary>
    /// Reflected CRC-16/IBM over uncompressed member bytes: polynomial
    /// <c>0xA001</c>, initial value zero, no final XOR.
    /// </summary>
    public static ushort Crc16(byte[] data, int offset, int count)
    {
        var crc = (ushort)0;
        for (var i = 0; i < count; i++)
        {
            crc = (ushort)((crc >> 8) ^ Crc16Table[(data[offset + i] ^ crc) & 0xFF]);
        }

        return crc;
    }

    private static ushort[] BuildCrc16Table()
    {
        var table = new ushort[256];
        for (var value = 0; value < 256; value++)
        {
            var entry = (ushort)value;
            for (var bit = 0; bit < 8; bit++)
            {
                entry = (entry & 1) == 1 ? (ushort)((entry >> 1) ^ 0xA001) : (ushort)(entry >> 1);
            }

            table[value] = entry;
        }

        return table;
    }
}
