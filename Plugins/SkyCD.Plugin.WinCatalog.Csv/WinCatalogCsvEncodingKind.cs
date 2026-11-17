namespace SkyCD.Plugin.WinCatalog.Csv;

/// <summary>
/// Character encodings a WinCatalog CSV report can be decoded with.
/// </summary>
public enum WinCatalogCsvEncodingKind
{
    /// <summary>
    /// The byte sequence does not match any supported profile.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// UTF-8 without a byte order mark.
    /// </summary>
    Utf8,

    /// <summary>
    /// UTF-8 introduced by a byte order mark.
    /// </summary>
    Utf8WithByteOrderMark,

    /// <summary>
    /// UTF-16 little endian, with or without a byte order mark.
    /// </summary>
    Utf16LittleEndian,

    /// <summary>
    /// UTF-16 big endian, with or without a byte order mark.
    /// </summary>
    Utf16BigEndian,

    /// <summary>
    /// Windows-1252, the "ANSI" code page used by legacy Windows exporters.
    /// </summary>
    Windows1252
}
