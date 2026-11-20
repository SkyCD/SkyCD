using System;
using System.Buffers.Binary;

namespace SkyCD.Plugin.Executable;

/// <summary>Raw values from the fixed part of a PE optional header that the parser needs.</summary>
internal readonly record struct PeHeader(
    bool IsPe32Plus,
    bool IsExecutableImage,
    uint ResourceRva,
    uint DataDirectoryCount,
    bool HasClrDirectory);

/// <summary>
/// Validates the DOS and PE headers and reads the optional-header fields the plugin depends on. It works on a
/// bounded byte window that already contains the section table, so a header claiming to be larger than the
/// window is rejected instead of trusted.
/// </summary>
internal static class PeHeaderReader
{
    /// <summary>The smallest window that can hold a DOS header and the PE signature offset.</summary>
    public const int MinimumHeaderBytes = 64;

    public static bool HasDosSignature(ReadOnlySpan<byte> window) =>
        window.Length >= 2 && window[0] == (byte)'M' && window[1] == (byte)'Z';

    /// <summary>File offset of the PE signature stored in the DOS header; null when it is negative.</summary>
    public static int? FindPeSignatureOffset(ReadOnlySpan<byte> window)
    {
        if (window.Length < 0x40)
        {
            return null;
        }

        var offset = BinaryPrimitives.ReadInt32LittleEndian(window.Slice(0x3C, 4));
        return offset < 0 ? null : offset;
    }

    public static bool HasPeSignature(ReadOnlySpan<byte> window, int peOffset) =>
        peOffset >= 0 && (long)peOffset + 4 <= window.Length &&
        window[peOffset] == (byte)'P' && window[peOffset + 1] == (byte)'E' &&
        window[peOffset + 2] == 0 && window[peOffset + 3] == 0;

    /// <summary>
    /// Reads the section count and the distance from the start of the file to the end of the section table, so
    /// the caller knows how much of the header must be read before the full parse can succeed.
    /// </summary>
    public static (int SectionCount, long HeaderLength) DescribeHeader(ReadOnlySpan<byte> window, int peOffset)
    {
        if ((long)peOffset + 24 > window.Length)
        {
            return (0, long.MaxValue);
        }

        var sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(window.Slice(peOffset + 6, 2));
        var optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(window.Slice(peOffset + 20, 2));
        return (sectionCount, peOffset + 24L + optionalSize + 40L * sectionCount);
    }

    /// <summary>Reads the machine type from the COFF header.</summary>
    public static ushort ReadMachine(ReadOnlySpan<byte> window, int peOffset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(window.Slice(peOffset + 4, 2));

    /// <summary>Reads the optional-header values the plugin uses; malformed shapes raise <see cref="FormatException"/>.</summary>
    public static PeHeader ReadOptionalHeader(ReadOnlySpan<byte> window, int peOffset)
    {
        var optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(window.Slice(peOffset + 20, 2));
        var optionalOffset = peOffset + 24;
        if (optionalSize < 68 || (long)optionalOffset + optionalSize > window.Length)
        {
            throw new FormatException("The optional header is truncated.");
        }

        var optional = window.Slice(optionalOffset, optionalSize);
        var magic = BinaryPrimitives.ReadUInt16LittleEndian(optional);
        var isPe32Plus = magic switch
        {
            0x20B => true,
            0x10B => false,
            _ => throw new FormatException($"Unsupported optional header magic 0x{magic:X4}.")
        };

        var characteristics = BinaryPrimitives.ReadUInt16LittleEndian(window.Slice(peOffset + 22, 2));
        // The resource entry is the third data directory; the first one is the export table at 0x70 or 0x60.
        var resourceRva = BinaryPrimitives.ReadUInt32LittleEndian(optional.Slice(isPe32Plus ? 0x80 : 0x70, 4));
        var directoryCountOffset = isPe32Plus ? 0x6C : 0x5C;
        var directoryCount = BinaryPrimitives.ReadUInt32LittleEndian(optional.Slice(directoryCountOffset, 4));

        var hasClrDirectory = directoryCount >= 15 &&
                              optional.Length >= directoryCountOffset + 4 + 8 * 15 &&
                              BinaryPrimitives.ReadUInt32LittleEndian(
                                  optional.Slice(directoryCountOffset + 4 + 8 * 14, 4)) != 0;

        return new PeHeader(isPe32Plus, (characteristics & 0x0002) != 0, resourceRva, directoryCount,
            hasClrDirectory);
    }
}
