using System;

namespace SkyCD.Plugin.Ace.Parsing;

/// <summary>
/// The ACE CRC-32: the standard reflected CRC-32 register without the final inversion, which makes it the
/// bitwise inverse of the usual CRC-32 value.
/// </summary>
internal static class AceCrc32
{
    private static readonly uint[] Table = BuildTable();

    /// <summary>Computes the ACE CRC-32 of <paramref name="data"/>.</summary>
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc;
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
