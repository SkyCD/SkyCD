namespace SkyCD.Plugin.DiskCatalogMaker.Models;

/// <summary>
/// Supported character encoding profiles for DiskCatalogMaker text exports.
/// </summary>
public enum DiskCatalogMakerEncodingKind
{
    Unknown = 0,
    Utf8,
    Utf8WithByteOrderMark,
    Utf16LittleEndian,
    Utf16BigEndian,
    Windows1252
}
