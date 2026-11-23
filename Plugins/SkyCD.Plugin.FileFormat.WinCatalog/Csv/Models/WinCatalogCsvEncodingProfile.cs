using System;
using System.Text;
using SkyCD.Plugin.EncodingProfiles;

namespace SkyCD.Plugin.FileFormat.WinCatalog.Csv.Models;

/// <summary>A strict decoding profile resolved for a WinCatalog CSV report.</summary>
public sealed class WinCatalogCsvEncodingProfile
{
    private readonly Encoding _encoding;

    private WinCatalogCsvEncodingProfile(WinCatalogCsvEncodingKind kind, string displayName, Encoding encoding)
    {
        Kind = kind;
        DisplayName = displayName;
        _encoding = encoding;
    }

    public WinCatalogCsvEncodingKind Kind { get; }
    public string DisplayName { get; }

    public static WinCatalogCsvEncodingProfile Utf8 { get; } =
        new(WinCatalogCsvEncodingKind.Utf8, "UTF-8", new UTF8Encoding(false, true));

    public static WinCatalogCsvEncodingProfile Utf8WithByteOrderMark { get; } =
        new(WinCatalogCsvEncodingKind.Utf8WithByteOrderMark, "UTF-8 with BOM", new UTF8Encoding(true, true));

    public static WinCatalogCsvEncodingProfile Utf16LittleEndian { get; } =
        new(WinCatalogCsvEncodingKind.Utf16LittleEndian, "UTF-16 LE", new UnicodeEncoding(false, true, true));

    public static WinCatalogCsvEncodingProfile Utf16BigEndian { get; } =
        new(WinCatalogCsvEncodingKind.Utf16BigEndian, "UTF-16 BE", new UnicodeEncoding(true, true, true));

    public static WinCatalogCsvEncodingProfile Windows1252 { get; } =
        new(WinCatalogCsvEncodingKind.Windows1252, "Windows-1252 (ANSI)",
            LegacyEncoding.StrictWindows1252);

    public static WinCatalogCsvEncodingProfile Unknown { get; } =
        new(WinCatalogCsvEncodingKind.Unknown, "Unknown", new UTF8Encoding(false, true));

    /// <summary>Decodes without replacement characters, stripping a BOM only when present.</summary>
    public string Decode(ReadOnlySpan<byte> data)
    {
        var preamble = _encoding.GetPreamble();
        var skip = preamble.Length > 0 && data.StartsWith(preamble) ? preamble.Length : 0;
        return _encoding.GetString(data[skip..]);
    }
}
