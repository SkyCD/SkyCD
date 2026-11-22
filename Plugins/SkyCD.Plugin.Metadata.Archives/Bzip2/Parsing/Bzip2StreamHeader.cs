using System;
using System.IO;

namespace SkyCD.Plugin.Metadata.Archives.Bzip2.Parsing;

/// <summary>
/// Validates the bzip2 stream header: the <c>BZh</c> magic followed by a block-size digit 1-9. Some
/// decoders silently accept trailing garbage, so damaged files are rejected before decoding starts.
/// </summary>
internal static class Bzip2StreamHeader
{
    private const int HeaderLength = 4;

    public static bool IsValid(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanSeek)
        {
            throw new ArgumentException("The bzip2 header requires a seekable stream.", nameof(source));
        }

        var origin = source.Position;
        try
        {
            source.Position = 0;
            Span<byte> header = stackalloc byte[HeaderLength];
            if (source.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
            {
                return false;
            }

            return Matches(header);
        }
        finally
        {
            source.Position = origin;
        }
    }

    public static bool Matches(ReadOnlySpan<byte> header)
    {
        return header.Length >= HeaderLength
               && header[0] == (byte)'B'
               && header[1] == (byte)'Z'
               && header[2] == (byte)'h'
               && header[3] is >= (byte)'1' and <= (byte)'9';
    }
}
