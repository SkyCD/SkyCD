using System;
using System.IO;

namespace SkyCD.Plugin.Metadata.Lha.Parsing;

/// <summary>
/// Detects whether a stream starts with an LHA member header. The archive reader treats an invalid
/// first header as the end of the archive, so damaged files must be rejected before it runs.
/// </summary>
internal static class LhaArchiveSignature
{
    private const int MethodOffset = 2;
    private const int MethodLength = 5;
    private const int HeaderBytes = MethodOffset + MethodLength;

    public static bool Matches(Stream source)
    {
        if (!source.CanSeek)
        {
            return true;
        }

        var origin = source.Position;
        Span<byte> header = stackalloc byte[HeaderBytes];
        try
        {
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

        if (header.Length < HeaderBytes)
        {
            return false;
        }

        return header[MethodOffset] == (byte)'-'
               && header[MethodOffset + 1] == (byte)'l'
               && char.IsAsciiLetterOrDigit((char)header[MethodOffset + 2])
               && char.IsAsciiLetterOrDigit((char)header[MethodOffset + 3])
               && header[MethodOffset + 4] == (byte)'-';
    }
}
