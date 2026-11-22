using System;
using System.Buffers.Binary;
using System.IO;

namespace SkyCD.Plugin.FileFormat.XZ.Parsing;

internal enum XzHeaderStatus
{
    Valid,
    Damaged,
    Unsupported
}

/// <summary>
/// Validates the fixed xz stream header: magic bytes, stream flags, and the header CRC32. The decoder
/// treats invalid input as an empty stream in some paths, so damaged files must be rejected here first.
/// </summary>
internal static class XzStreamHeader
{
    private const int HeaderLength = 12;
    private const int FlagsOffset = 6;
    private const int CrcLength = 4;
    private const byte ReservedFlagMask = 0xF0;
    private const byte CheckIdMask = 0x0F;

    private static readonly byte[] Magic = [0xFD, (byte)'7', (byte)'z', (byte)'X', (byte)'Z', 0x00];

    public static XzHeaderStatus Read(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanSeek)
        {
            throw new ArgumentException("The xz header requires a seekable stream.", nameof(source));
        }

        var origin = source.Position;
        try
        {
            source.Position = 0;
            Span<byte> header = stackalloc byte[HeaderLength];
            if (source.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
            {
                return XzHeaderStatus.Damaged;
            }

            if (!header[..Magic.Length].SequenceEqual(Magic))
            {
                return XzHeaderStatus.Damaged;
            }

            var flags = header.Slice(FlagsOffset, 2);
            var expectedCrc = BinaryPrimitives.ReadUInt32LittleEndian(header[(FlagsOffset + 2)..]);
            if (Crc32(flags) != expectedCrc)
            {
                return XzHeaderStatus.Damaged;
            }

            // Stream flags: the first byte is reserved; the second byte holds the check id in its low
            // nibble (0 none, 1 CRC32, 4 CRC64, 10 SHA-256) and reserved bits above.
            var checkId = (byte)(flags[1] & CheckIdMask);
            if (flags[0] != 0 || (flags[1] & ReservedFlagMask) != 0 || checkId is not (0 or 1 or 4 or 10))
            {
                return XzHeaderStatus.Unsupported;
            }

            return XzHeaderStatus.Valid;
        }
        finally
        {
            source.Position = origin;
        }
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }
}
