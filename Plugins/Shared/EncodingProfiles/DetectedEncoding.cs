using System;
using System.Text;

namespace SkyCD.Plugin.EncodingProfiles;

/// <summary>
/// A character encoding resolved for a legacy catalog export, together with the decoder that reads it.
/// </summary>
/// <remarks>
/// The decoder never substitutes a replacement character. It is created with exception fallbacks, so a
/// payload that does not actually match the detected encoding fails the import instead of being imported
/// as mojibake.
/// </remarks>
internal sealed record DetectedEncoding(
    string Name,
    string DisplayName,
    Encoding Encoding,
    float Confidence,
    bool HasByteOrderMark)
{
    /// <summary>Decodes the payload without replacement characters, stripping a byte order mark when present.</summary>
    public string Decode(ReadOnlySpan<byte> data)
    {
        var skip = ByteOrderMark.Length(data);
        return Encoding.GetString(data[skip..]);
    }
}
