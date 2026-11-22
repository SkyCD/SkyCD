using System;
using System.Buffers.Binary;

namespace SkyCD.Plugin.Metadata.Executable.Parsing;

/// <summary>
/// Recognises whether a source is a portable executable before it is handed to
/// <see cref="System.Reflection.PortableExecutable.PEHeaders"/>. The optional header and the section table
/// themselves are read by the BCL reader, not here.
/// </summary>
internal static class PeContainerProbe
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
    /// Reads the section count from the COFF header, so the caller can reject an implausible section table before
    /// the BCL reader allocates it. A window too short to hold the field yields zero.
    /// </summary>
    public static int ReadSectionCount(ReadOnlySpan<byte> window, int peOffset) =>
        (long)peOffset + 8 <= window.Length
            ? BinaryPrimitives.ReadUInt16LittleEndian(window.Slice(peOffset + 6, 2))
            : 0;
}
