using System;

namespace SkyCD.Plugin.EncodingProfiles;

/// <summary>
/// Recognises the byte order mark of a payload.
/// </summary>
/// <remarks>
/// The markers are matched on the raw bytes instead of through <see cref="System.Text.Encoding.GetPreamble"/>,
/// because the preamble of a code page is not always the marker that introduces the payload: the
/// GB18030 marker is not reported by .NET, and the UTF-8 preamble is only reported by some encodings.
/// </remarks>
internal static class ByteOrderMark
{
    /// <summary>
    /// Returns the length of the byte order mark the payload starts with, or zero when it has none.
    /// </summary>
    public static int Length(ReadOnlySpan<byte> data) => data switch
    {
        [0x00, 0x00, 0xFE, 0xFF, ..] => 4,
        [0xFF, 0xFE, 0x00, 0x00, ..] => 4,
        [0x84, 0x31, 0x95, 0x33, ..] => 4,
        [0xEF, 0xBB, 0xBF, ..] => 3,
        [0xFE, 0xFF, ..] => 2,
        [0xFF, 0xFE, ..] => 2,
        _ => 0
    };

    /// <summary>
    /// Returns the name of the encoding the byte order mark introduces, or null when the payload has no marker.
    /// </summary>
    public static string? EncodingName(ReadOnlySpan<byte> data) => data switch
    {
        [0x00, 0x00, 0xFE, 0xFF, ..] => "utf-32be",
        [0xFF, 0xFE, 0x00, 0x00, ..] => "utf-32le",
        [0xEF, 0xBB, 0xBF, ..] => "utf-8",
        [0xFE, 0xFF, ..] => "utf-16be",
        [0xFF, 0xFE, ..] => "utf-16le",
        _ => null
    };
}
