using System;
using System.Buffers.Binary;
using SkyCD.Plugin.Metadata.GameDev.Models;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>
/// Reads the 16-byte Wicked Engine scene header: a 64-bit version followed by a property word whose low 32
/// bits are the optional JPEG thumbnail length. Because the format has no magic, the header is only accepted
/// when both words are plausible and the thumbnail, if present, starts with a JPEG signature.
/// </summary>
internal static class WickedReader
{
    private const int HeaderLength = 16;

    internal static GameDevFacts? Read(byte[] data)
    {
        if (data.Length < HeaderLength)
        {
            return null;
        }

        var version = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(0, 8));
        var properties = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(8, 8));
        var thumbnailLength = (uint)(properties & 0xFFFFFFFF);
        var upper = properties >> 32;

        if (version == 0 || version > 1_000_000 || upper > 0xFFFF || thumbnailLength > GameDevLimits.MaxArtifactBytes)
        {
            return null;
        }

        var hasThumbnail = thumbnailLength >= 2
            && data.Length >= HeaderLength + thumbnailLength
            && GameDevSource.StartsWith(data.AsSpan(HeaderLength), 0xFF, 0xD8);

        return new GameDevFacts
        {
            Family = "Wicked Engine",
            Format = "Wicked scene",
            FormatVersion = version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            HasThumbnail = hasThumbnail
        };
    }

    /// <summary>Passes the embedded JPEG through when it is valid and within the artifact bound.</summary>
    internal static byte[]? TryReadThumbnail(byte[] data)
    {
        if (data.Length < HeaderLength)
        {
            return null;
        }

        var properties = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(8, 8));
        var length = (uint)(properties & 0xFFFFFFFF);
        if (length < 2 || length > GameDevLimits.MaxArtifactBytes || data.Length < HeaderLength + length)
        {
            return null;
        }

        var start = HeaderLength;
        if (!GameDevSource.StartsWith(data.AsSpan(start), 0xFF, 0xD8))
        {
            return null;
        }

        return data.AsSpan(start, (int)length).ToArray();
    }
}
