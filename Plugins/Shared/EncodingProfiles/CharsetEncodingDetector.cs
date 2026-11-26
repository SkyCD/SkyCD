using System;
using System.Text;
using UtfUnknown;

namespace SkyCD.Plugin.EncodingProfiles;

internal static class CharsetEncodingDetector
{
    private const int Utf16ProbeLength = 512;

    static CharsetEncodingDetector() =>
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static DetectionDetail? Detect(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return null;
        }

        if (!HasByteOrderMark(data))
        {
            var utf16 = TryDetectUtf16WithoutByteOrderMark(data);
            if (utf16 is not null)
            {
                return utf16;
            }
        }

        var detection = CharsetDetector.DetectFromBytes(data).Detected;
        if (detection?.Encoding is not { } detectedEncoding ||
            !IsSupportedCodePage(detectedEncoding.CodePage) ||
            (detectedEncoding.CodePage is 1252 or 28591 && !IsValidWindows1252(data)))
        {
            return null;
        }

        var encoding = detectedEncoding.CodePage == 28591
            ? Encoding.GetEncoding(1252)
            : detectedEncoding;
        var strictEncoding = (Encoding)encoding.Clone();
        strictEncoding.DecoderFallback = DecoderFallback.ExceptionFallback;
        detection.Encoding = strictEncoding;
        return detection;
    }

    public static string Decode(DetectionDetail detection, ReadOnlySpan<byte> data)
    {
        var text = detection.Encoding.GetString(data);
        return detection.HasBOM && text.StartsWith("\uFEFF", StringComparison.Ordinal)
            ? text[1..]
            : text;
    }

    private static bool HasByteOrderMark(ReadOnlySpan<byte> data) =>
        (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) ||
        (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE) ||
        (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF) ||
        (data.Length >= 4 && data[0] == 0x00 && data[1] == 0x00 && data[2] == 0xFE && data[3] == 0xFF);

    private static DetectionDetail? TryDetectUtf16WithoutByteOrderMark(ReadOnlySpan<byte> data)
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

        if (oddNulls > 0 && evenNulls == 0)
        {
            return new DetectionDetail("utf-16le", 1f)
            {
                Encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true),
                HasBOM = false
            };
        }

        if (evenNulls > 0 && oddNulls == 0)
        {
            return new DetectionDetail("utf-16be", 1f)
            {
                Encoding = new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true),
                HasBOM = false
            };
        }

        return null;
    }

    private static bool IsSupportedCodePage(int codePage) =>
        codePage is 20127 or 65001 or 1200 or 1201 or 1251 or 1252 or 932 or 54936 ||
        codePage is >= 28591 and <= 28606;

    private static bool IsValidWindows1252(ReadOnlySpan<byte> data)
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
}
