using System;
using System.IO;

namespace SkyCD.Plugin.Zstd;

/// <summary>Classifies the first bytes of a Zstandard stream.</summary>
internal enum ZstdHeaderStatus
{
    Valid,
    Damaged,
    Unsupported
}

/// <summary>
/// Validates the Zstandard frame prefix: the frame magic followed by a frame header descriptor whose
/// reserved, unused, and dictionary-identifier fields are within the current specification.
/// </summary>
internal static class ZstdFrameHeader
{
    public const int MinimumLength = 5;
    private const uint FrameMagic = 0xFD2FB528;
    private const uint SkippableMagic = 0x184D2A50;
    private const uint SkippableMagicMask = 0xFFFFFFF0;
    private const byte ReservedBit = 0b0000_1000;
    private const byte UnusedBit = 0b0001_0000;
    private const byte DictionaryIdMask = 0b0000_0011;
    private const byte ReservedDictionaryId = 0b0000_0011;

    public static ZstdHeaderStatus Read(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanSeek)
        {
            throw new ArgumentException("The Zstandard header requires a seekable stream.", nameof(source));
        }

        var origin = source.Position;
        try
        {
            source.Position = 0;
            Span<byte> header = stackalloc byte[MinimumLength];
            if (source.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
            {
                return ZstdHeaderStatus.Damaged;
            }

            return Classify(header);
        }
        finally
        {
            source.Position = origin;
        }
    }

    public static ZstdHeaderStatus Classify(ReadOnlySpan<byte> header)
    {
        if (header.Length < MinimumLength)
        {
            return ZstdHeaderStatus.Damaged;
        }

        var magic = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header);
        if ((magic & SkippableMagicMask) == SkippableMagic)
        {
            return ZstdHeaderStatus.Unsupported;
        }

        if (magic != FrameMagic)
        {
            return ZstdHeaderStatus.Damaged;
        }

        var descriptor = header[4];
        if ((descriptor & (ReservedBit | UnusedBit)) != 0
            || (descriptor & DictionaryIdMask) == ReservedDictionaryId)
        {
            return ZstdHeaderStatus.Unsupported;
        }

        return ZstdHeaderStatus.Valid;
    }
}
