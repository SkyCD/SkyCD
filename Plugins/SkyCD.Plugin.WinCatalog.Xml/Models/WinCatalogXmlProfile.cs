using System;
using System.Collections.Generic;
using SkyCD.Plugin.WinCatalog.Csv;
using SkyCD.Plugin.WinCatalog.Csv.Models;

namespace SkyCD.Plugin.WinCatalog.Xml.Models;

/// <summary>
/// Maps WinCatalog XML element and attribute names to the semantic columns shared
/// with the WinCatalog CSV importer (<see cref="WinCatalogCsvProfile"/>), plus
/// XML-report specific aliases.
/// </summary>
public static class WinCatalogXmlProfile
{
    private static readonly Dictionary<string, WinCatalogCsvColumn> ExtraAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // Display name aliases used in XML exports
        ["Title"] = WinCatalogCsvColumn.Name,

        // Volume aliases
        ["Volume Name"] = WinCatalogCsvColumn.VolumeLabel,
        ["Volume Label"] = WinCatalogCsvColumn.VolumeLabel,
        ["VolumeLabel"] = WinCatalogCsvColumn.VolumeLabel,
        ["Volume_Name"] = WinCatalogCsvColumn.VolumeLabel,
        ["Volume_Label"] = WinCatalogCsvColumn.VolumeLabel,

        // Type aliases
        ["Item Type"] = WinCatalogCsvColumn.Type,
        ["File Type"] = WinCatalogCsvColumn.Type,

        // Date aliases
        ["Last Modified"] = WinCatalogCsvColumn.Modified,
        ["Date"] = WinCatalogCsvColumn.Modified,

        // Comment aliases
        ["Notes"] = WinCatalogCsvColumn.Comments,
        ["Description"] = WinCatalogCsvColumn.Comments,

        // Tag aliases
        ["Tags_associated"] = WinCatalogCsvColumn.Tags,
        ["Tags (associated)"] = WinCatalogCsvColumn.Tags,
        ["TagsAssociated"] = WinCatalogCsvColumn.Tags,

        // Location aliases
        ["Physical_location"] = WinCatalogCsvColumn.PhysicalLocation,
        ["PhysicalLocation"] = WinCatalogCsvColumn.PhysicalLocation,

        // Checksum aliases
        ["MD5 Hash"] = WinCatalogCsvColumn.MD5,
        ["SHA256 Sum"] = WinCatalogCsvColumn.SHA256,
        ["CRC32 Sum"] = WinCatalogCsvColumn.CRC32,
        ["MD5___SHA256___Blake2"] = WinCatalogCsvColumn.MD5,
        ["MD5 / SHA256 / Blake2"] = WinCatalogCsvColumn.MD5
    };

    /// <summary>
    /// Maps a raw XML element or attribute name to its semantic WinCatalog column.
    /// </summary>
    public static WinCatalogCsvColumn MapField(string name)
    {
        var extra = ExtraAliases.TryGetValue(name.Trim(), out var column)
            ? column
            : WinCatalogCsvColumn.Unknown;
        return extra != WinCatalogCsvColumn.Unknown ? extra : WinCatalogCsvProfile.MapColumn(name);
    }
}
