namespace SkyCD.Plugin.FileFormat.AbeMeda.Text.Models;

/// <summary>
/// Character encoding kinds supported for NeoFinder text exports.
/// </summary>
public enum NeoFinderTextEncodingKind
{
    Unknown = 0,
    Utf8 = 1,
    Utf8WithByteOrderMark = 2,
    Utf16LittleEndian = 3,
    Utf16BigEndian = 4,
    Windows1252 = 5
}
