using System;
using System.IO;

namespace SkyCD.Plugin.Metadata.Rar.Parsing;

/// <summary>
/// Recognizes RAR4 and RAR5 archives from their seven byte signature. The archive reader reports an
/// invalid signature as an end of archive, so unrecognized files must be rejected before it runs.
/// </summary>
internal static class RarArchiveSignature
{
    /// <summary>Gets the number of bytes in a RAR signature block, including the version byte.</summary>
    public const int SignatureLength = 8;

    /// <summary>Gets the signature byte that identifies a RAR5 archive.</summary>
    public const byte Rar5Version = 1;

    private static readonly byte[] Marker = [(byte)'R', (byte)'a', (byte)'r', (byte)'!', 0x1A, 0x07];

    public static bool Matches(Stream source)
    {
        if (!source.CanSeek)
        {
            return true;
        }

        var origin = source.Position;
        Span<byte> header = stackalloc byte[SignatureLength];
        try
        {
            source.Position = 0;
            var read = source.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
            return Matches(header[..read]);
        }
        finally
        {
            source.Position = origin;
        }
    }

    public static bool Matches(ReadOnlySpan<byte> header)
    {
        if (header.Length == 0)
        {
            return true;
        }

        if (header.Length < SignatureLength)
        {
            return false;
        }

        if (!header[..Marker.Length].SequenceEqual(Marker))
        {
            return false;
        }

        return header[6] == 0 || header[6] == Rar5Version;
    }
}
