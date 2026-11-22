using System;

namespace SkyCD.Plugin.Metadata.Archives.Zoo.Parsing;

/// <summary>
/// Incremental CRC-16/ARC (polynomial 0x8005, reflected, initial value zero) used by ZOO directory entries.
/// </summary>
internal sealed class ZooCrc16
{
    private static readonly ushort[] Table = BuildTable();
    private ushort value;

    /// <summary>Gets the CRC accumulated so far.</summary>
    public ushort Value => value;

    /// <summary>Adds one byte to the running CRC.</summary>
    public void Append(byte data) => value = (ushort)((value >> 8) ^ Table[(value ^ data) & 0xFF]);

    /// <summary>Adds a byte sequence to the running CRC.</summary>
    public void Append(ReadOnlySpan<byte> data)
    {
        foreach (var item in data)
        {
            Append(item);
        }
    }

    /// <summary>Computes the CRC of a byte sequence.</summary>
    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        var crc = new ZooCrc16();
        crc.Append(data);
        return crc.Value;
    }

    private static ushort[] BuildTable()
    {
        var table = new ushort[256];
        for (var index = 0; index < table.Length; index++)
        {
            var entry = (ushort)index;
            for (var bit = 0; bit < 8; bit++)
            {
                entry = (ushort)((entry & 1) != 0 ? (entry >> 1) ^ 0xA001 : entry >> 1);
            }

            table[index] = entry;
        }

        return table;
    }
}
