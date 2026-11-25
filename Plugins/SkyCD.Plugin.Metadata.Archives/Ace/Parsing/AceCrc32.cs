using System;

namespace SkyCD.Plugin.Metadata.Archives.Ace.Parsing;

/// <summary>
/// The ACE CRC-32: the standard reflected CRC-32 register without the final inversion. The value is built
/// incrementally so an entry can be checksummed while it is copied to disk.
/// </summary>
internal sealed class AceCrc32
{
    private static readonly uint[] Table = BuildTable();

    private uint crc = 0xFFFFFFFFu;

    /// <summary>Gets the checksum of everything appended so far.</summary>
    public uint Value => crc;

    /// <summary>Adds the next bytes of the entry to the checksum.</summary>
    public void Append(ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (var index = 0; index < table.Length; index++)
        {
            var value = (uint)index;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? (value >> 1) ^ 0xEDB88320u : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }
}
