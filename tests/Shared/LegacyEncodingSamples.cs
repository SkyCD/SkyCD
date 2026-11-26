using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SkyCD.TestSupport;

internal static class LegacyEncodingSamples
{
    public static IEnumerable<object[]> Cases =>
    [
        ["utf-8", false, "Caf\u00E9 d\u00E9j\u00E0 vu"],
        ["utf-8", true, "Caf\u00E9 d\u00E9j\u00E0 vu"],
        ["utf-16le", false, "Caf\u00E9 d\u00E9j\u00E0 vu"],
        ["utf-16be", false, "Caf\u00E9 d\u00E9j\u00E0 vu"],
        ["windows-1252", false, "Caf\u00E9 d\u00E9j\u00E0 vu"],
        ["windows-1251", false, "\u041F\u0440\u0438\u0432\u0435\u0442 \u043C\u0438\u0440 \u041F\u0440\u0438\u0432\u0435\u0442 \u043C\u0438\u0440"],
        ["shift_jis", false, "\u65E5\u672C\u8A9E\u306E\u6587\u5B57\u30B3\u30FC\u30C9\u691C\u51FA\u3067\u3059\u65E5\u672C\u8A9E"],
        ["gb18030", false, "\u7B80\u4F53\u4E2D\u6587\u7F16\u7801\u68C0\u6D4B\u6587\u672C\u7B80\u4F53\u4E2D\u6587"]
    ];

    static LegacyEncodingSamples()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static byte[] Encode(string encodingName, bool includeByteOrderMark, string text)
    {
        var encoding = encodingName == "utf-8"
            ? new UTF8Encoding(includeByteOrderMark, throwOnInvalidBytes: true)
            : Encoding.GetEncoding(
                encodingName,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
        var preamble = includeByteOrderMark ? encoding.GetPreamble() : [];
        return preamble.Concat(encoding.GetBytes(text)).ToArray();
    }
}
