using System;
using System.Text;

namespace SkyCD.Plugin.WinCatalog.Csv;

/// <summary>
/// Resolves the character encoding profile of a WinCatalog CSV report.
/// </summary>
/// <remarks>
/// <para>
/// A byte order mark is authoritative and always wins. Without one the profile is decided by content:
/// UTF-16 is recognised from the alternating NUL pattern of a leading ASCII header, UTF-8 is accepted only
/// when every byte is a valid UTF-8 sequence, and Windows-1252 is accepted when the bytes are not valid UTF-8
/// but are valid Windows-1252.
/// </para>
/// <para>
/// Detection is deliberately free of lossy decoding. A byte sequence that matches no supported profile is
/// reported as <see cref="WinCatalogCsvEncodingProfile.Unknown"/> so the caller can fail with a clear message
/// instead of importing a report full of replacement characters.
/// </para>
/// </remarks>
public static class WinCatalogCsvEncodingDetector
{
    private static readonly byte[] Utf8ByteOrderMark = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] Utf16LittleEndianByteOrderMark = [0xFF, 0xFE];
    private static readonly byte[] Utf16BigEndianByteOrderMark = [0xFE, 0xFF];
    private static readonly byte[] Utf32LittleEndianByteOrderMark = [0xFF, 0xFE, 0x00, 0x00];
    private static readonly byte[] Utf32BigEndianByteOrderMark = [0x00, 0x00, 0xFE, 0xFF];

    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// The number of leading bytes inspected when looking for the NUL pattern of a UTF-16 header.
    /// </summary>
    private const int Utf16ProbeLength = 512;

    /// <summary>
    /// Detects the encoding profile of the supplied bytes.
    /// </summary>
    public static WinCatalogCsvEncodingProfile Detect(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return WinCatalogCsvEncodingProfile.Unknown;
        }

        // UTF-32 is checked first because its little endian marker starts with the UTF-16 little endian marker.
        // UTF-32 is not a supported profile, so it is reported as unknown rather than mis-decoded.
        if (StartsWith(data, Utf32LittleEndianByteOrderMark) || StartsWith(data, Utf32BigEndianByteOrderMark))
        {
            return WinCatalogCsvEncodingProfile.Unknown;
        }

        if (StartsWith(data, Utf8ByteOrderMark))
        {
            return WinCatalogCsvEncodingProfile.Utf8WithByteOrderMark;
        }

        if (StartsWith(data, Utf16LittleEndianByteOrderMark))
        {
            return WinCatalogCsvEncodingProfile.Utf16LittleEndian;
        }

        if (StartsWith(data, Utf16BigEndianByteOrderMark))
        {
            return WinCatalogCsvEncodingProfile.Utf16BigEndian;
        }

        var utf16 = DetectUtf16WithoutByteOrderMark(data);
        if (utf16 is not null)
        {
            return utf16;
        }

        if (IsValidUtf8(data))
        {
            return WinCatalogCsvEncodingProfile.Utf8;
        }

        return IsValidWindows1252(data)
            ? WinCatalogCsvEncodingProfile.Windows1252
            : WinCatalogCsvEncodingProfile.Unknown;
    }

    /// <summary>
    /// Determines whether every byte is a valid UTF-8 sequence.
    /// </summary>
    public static bool IsValidUtf8(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return true;
        }

        try
        {
            _ = StrictUtf8.GetString(data);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// Determines whether every byte maps to a Windows-1252 character.
    /// </summary>
    /// <remarks>
    /// Windows-1252 leaves five positions undefined. Bytes landing on them cannot be decoded, so a report
    /// containing them matches no supported profile.
    /// </remarks>
    public static bool IsValidWindows1252(ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            if (value is 0x81 or 0x8D or 0x8F or 0x90 or 0x9D)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Detects UTF-16 without a byte order mark from the NUL pattern of the leading header.
    /// </summary>
    private static WinCatalogCsvEncodingProfile? DetectUtf16WithoutByteOrderMark(ReadOnlySpan<byte> data)
    {
        var evenNulls = 0;
        var oddNulls = 0;
        var limit = Math.Min(data.Length, Utf16ProbeLength);

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

        // UTF-16 little endian places the NUL of an ASCII character on the odd byte, big endian on the even one.
        if (oddNulls > 0 && evenNulls == 0)
        {
            return WinCatalogCsvEncodingProfile.Utf16LittleEndian;
        }

        return evenNulls > 0 && oddNulls == 0
            ? WinCatalogCsvEncodingProfile.Utf16BigEndian
            : null;
    }

    private static bool StartsWith(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prefix) =>
        data.Length >= prefix.Length && data[..prefix.Length].SequenceEqual(prefix);
}
