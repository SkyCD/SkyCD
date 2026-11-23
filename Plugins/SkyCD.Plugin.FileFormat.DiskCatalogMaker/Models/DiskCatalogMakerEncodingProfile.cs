using System;
using System.Text;
using SkyCD.Plugin.EncodingProfiles;

namespace SkyCD.Plugin.FileFormat.DiskCatalogMaker.Models;

/// <summary>A strict decoding profile resolved for a DiskCatalogMaker text export.</summary>
public sealed class DiskCatalogMakerEncodingProfile
{
    private readonly Encoding _encoding;

    private DiskCatalogMakerEncodingProfile(DiskCatalogMakerEncodingKind kind, string displayName, Encoding encoding)
    {
        Kind = kind;
        DisplayName = displayName;
        _encoding = encoding;
    }

    public DiskCatalogMakerEncodingKind Kind { get; }
    public string DisplayName { get; }

    public static DiskCatalogMakerEncodingProfile Utf8 { get; } =
        new(DiskCatalogMakerEncodingKind.Utf8, "UTF-8", new UTF8Encoding(false, true));

    public static DiskCatalogMakerEncodingProfile Utf8WithByteOrderMark { get; } =
        new(DiskCatalogMakerEncodingKind.Utf8WithByteOrderMark, "UTF-8 with BOM", new UTF8Encoding(true, true));

    public static DiskCatalogMakerEncodingProfile Utf16LittleEndian { get; } =
        new(DiskCatalogMakerEncodingKind.Utf16LittleEndian, "UTF-16 LE", new UnicodeEncoding(false, true, true));

    public static DiskCatalogMakerEncodingProfile Utf16BigEndian { get; } =
        new(DiskCatalogMakerEncodingKind.Utf16BigEndian, "UTF-16 BE", new UnicodeEncoding(true, true, true));

    public static DiskCatalogMakerEncodingProfile Windows1252 { get; } =
        new(DiskCatalogMakerEncodingKind.Windows1252, "Windows-1252 (ANSI)",
            LegacyEncoding.StrictWindows1252);

    public static DiskCatalogMakerEncodingProfile Unknown { get; } =
        new(DiskCatalogMakerEncodingKind.Unknown, "Unknown", new UTF8Encoding(false, true));

    /// <summary>Decodes without replacement characters, stripping a BOM only when present.</summary>
    public string Decode(ReadOnlySpan<byte> data)
    {
        var preamble = _encoding.GetPreamble();
        var skip = preamble.Length > 0 && data.StartsWith(preamble) ? preamble.Length : 0;
        return _encoding.GetString(data[skip..]);
    }
}
