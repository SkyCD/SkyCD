using System;
using System.Text;
using UtfUnknown;

namespace SkyCD.Plugin.EncodingProfiles;

/// <summary>
/// Resolves the character encoding of a legacy catalog export.
/// </summary>
/// <remarks>
/// <para>
/// Content based detection is delegated to UTF.Unknown, a port of the Mozilla universal charset detector.
/// It recognises the legacy code pages the previous hand-rolled heuristics missed, such as Windows-1251,
/// Shift-JIS and GB18030.
/// </para>
/// <para>
/// Two rules are applied around it, because a statistical detector is a language model rather than a
/// format detector:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// A byte order mark is authoritative and always wins.
/// </description>
/// </item>
/// <item>
/// <description>
/// UTF-16 without a byte order mark is recognised from the alternating NUL pattern of the payload. The
/// statistical detector reports such a payload as an 8-bit code page, which would decode a UTF-16 report
/// into mojibake.
/// </description>
/// </item>
/// </list>
/// <para>
/// The resolved decoder uses exception fallbacks, so bytes that do not match the detected encoding fail
/// the import with a clear error instead of being replaced by U+FFFD.
/// </para>
/// </remarks>
internal static class LegacyEncodingDetector
{
    /// <summary>
    /// The number of leading bytes inspected when looking for the NUL pattern of a UTF-16 payload.
    /// </summary>
    private const int Utf16ProbeLength = 512;

    /// <summary>
    /// The number of NUL bytes a UTF-16 payload must carry before the pattern is trusted. A single stray
    /// NUL byte in an 8-bit export is noise, not a UTF-16 marker.
    /// </summary>
    private const int MinimumUtf16NullBytes = 2;

    /// <summary>
    /// Detects the encoding of the supplied bytes, or returns null when no encoding could be resolved.
    /// </summary>
    public static DetectedEncoding? Detect(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return null;
        }

        var fromByteOrderMark = DetectFromByteOrderMark(data);
        if (fromByteOrderMark is not null)
        {
            return fromByteOrderMark;
        }

        var detected = DetectFromContent(data);
        if (detected is not null && !detected.Encoding.IsSingleByte)
        {
            return detected;
        }

        // Only an 8-bit code page can be the wrong answer for a UTF-16 payload, so the NUL pattern is
        // inspected when the detector named a single byte code page or named nothing at all.
        return DetectUtf16WithoutByteOrderMark(data) ?? detected;
    }

    /// <summary>
    /// Resolves the encoding a leading byte order mark names.
    /// </summary>
    private static DetectedEncoding? DetectFromByteOrderMark(ReadOnlySpan<byte> data)
    {
        var name = ByteOrderMark.EncodingName(data);
        return name is null ? null : Create(name, confidence: 1f, hasByteOrderMark: true);
    }

    /// <summary>
    /// Resolves the encoding from the content of the payload.
    /// </summary>
    private static DetectedEncoding? DetectFromContent(ReadOnlySpan<byte> data)
    {
        var details = CharsetDetector.DetectFromBytes(data).Details;
        if (details is null || details.Count == 0)
        {
            return null;
        }

        var selected = details[0];

        // ASCII is a subset of UTF-8, and the UTF-8 decoder is the stricter of the two, so an ASCII
        // payload is decoded as UTF-8 and reported as such.
        //
        // ISO-8859-1 and ISO-8859-9 carry the Latin-1 repertoire above 0x9F and leave the 0x80-0x9F
        // range undefined. Windows-1252 keeps that repertoire and maps the undefined positions to
        // printable characters, so it decodes the same bytes into readable text. The other ISO-8859
        // parts are left alone, because their repertoire above 0x9F differs from Windows-1252.
        var name = selected.EncodingName switch
        {
            "ascii" => "utf-8",
            "iso-8859-1" or "iso-8859-9" => "windows-1252",
            _ => selected.EncodingName
        };

        return Create(name, selected.Confidence, selected.HasBOM);
    }

    /// <summary>
    /// Recognises UTF-16 without a byte order mark from the NUL pattern of the leading bytes.
    /// </summary>
    /// <remarks>
    /// UTF-16 little endian places the NUL of an ASCII character on the odd byte, big endian on the even
    /// one. A payload that mixes both parities is not UTF-16.
    /// </remarks>
    private static DetectedEncoding? DetectUtf16WithoutByteOrderMark(ReadOnlySpan<byte> data)
    {
        var limit = Math.Min(data.Length, Utf16ProbeLength);
        var evenNulls = 0;
        var oddNulls = 0;

        for (var index = 0; index < limit; index++)
        {
            if (data[index] != 0)
            {
                continue;
            }

            if (index % 2 == 0)
            {
                evenNulls++;
            }
            else
            {
                oddNulls++;
            }
        }

        if (evenNulls == 0 && oddNulls >= MinimumUtf16NullBytes)
        {
            return Create("utf-16le", confidence: 1f, hasByteOrderMark: false);
        }

        return oddNulls == 0 && evenNulls >= MinimumUtf16NullBytes
            ? Create("utf-16be", confidence: 1f, hasByteOrderMark: false)
            : null;
    }

    /// <summary>
    /// Builds the decoding profile for an encoding name.
    /// </summary>
    private static DetectedEncoding? Create(string name, float confidence, bool hasByteOrderMark)
    {
        Encoding encoding;
        try
        {
            encoding = LegacyEncoding.GetStrict(name);
        }
        catch (ArgumentException)
        {
            // The platform does not know the code page the detector named.
            return null;
        }

        return new DetectedEncoding(
            name,
            GetDisplayName(name, hasByteOrderMark),
            encoding,
            confidence,
            hasByteOrderMark);
    }

    /// <summary>
    /// Returns the label the import summary reports the encoding under.
    /// </summary>
    private static string GetDisplayName(string name, bool hasByteOrderMark) => name switch
    {
        "utf-8" => hasByteOrderMark ? "UTF-8 with BOM" : "UTF-8",
        "utf-16le" => "UTF-16 LE",
        "utf-16be" => "UTF-16 BE",
        "utf-32le" => "UTF-32 LE",
        "utf-32be" => "UTF-32 BE",
        "windows-1252" => "Windows-1252 (ANSI)",
        "windows-1251" => "Windows-1251",
        "shift-jis" => "Shift-JIS",
        "gb18030" => "GB18030",
        "iso-8859-1" => "ISO-8859-1",
        _ => name
    };
}
