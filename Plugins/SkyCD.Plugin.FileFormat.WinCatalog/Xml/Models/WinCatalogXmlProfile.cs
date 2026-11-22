using System;
using System.Collections.Generic;
using SkyCD.Plugin.FileFormat.WinCatalog.Csv;
using SkyCD.Plugin.FileFormat.WinCatalog.Csv.Models;
using SkyCD.Plugin.FileFormat.WinCatalog.Models;

namespace SkyCD.Plugin.FileFormat.WinCatalog.Xml.Models;

/// <summary>
/// Maps WinCatalog XML element and attribute names to the semantic columns shared
/// with the WinCatalog CSV importer (<see cref="WinCatalogProfile"/>), plus
/// XML-report specific aliases.
/// </summary>
public static class WinCatalogXmlProfile
{
    private static readonly Dictionary<string, WinCatalogColumn> ExtraAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // Display name aliases used in XML exports
        ["Title"] = WinCatalogColumn.Name,

        // Volume aliases
        ["Volume Name"] = WinCatalogColumn.VolumeLabel,
        ["Volume Label"] = WinCatalogColumn.VolumeLabel,
        ["VolumeLabel"] = WinCatalogColumn.VolumeLabel,
        ["Volume_Name"] = WinCatalogColumn.VolumeLabel,
        ["Volume_Label"] = WinCatalogColumn.VolumeLabel,

        // Type aliases
        ["Item Type"] = WinCatalogColumn.Type,
        ["File Type"] = WinCatalogColumn.Type,

        // Date aliases
        ["Last Modified"] = WinCatalogColumn.Modified,
        ["Date"] = WinCatalogColumn.Modified,

        // Comment aliases
        ["Notes"] = WinCatalogColumn.Comments,
        ["Description"] = WinCatalogColumn.Comments,

        // Tag aliases
        ["Tags_associated"] = WinCatalogColumn.Tags,
        ["Tags (associated)"] = WinCatalogColumn.Tags,
        ["TagsAssociated"] = WinCatalogColumn.Tags,

        // Location aliases
        ["Physical_location"] = WinCatalogColumn.PhysicalLocation,
        ["PhysicalLocation"] = WinCatalogColumn.PhysicalLocation,

        // Checksum aliases
        ["MD5 Hash"] = WinCatalogColumn.MD5,
        ["SHA256 Sum"] = WinCatalogColumn.SHA256,
        ["CRC32 Sum"] = WinCatalogColumn.CRC32,
        ["MD5___SHA256___Blake2"] = WinCatalogColumn.MD5,
        ["MD5 / SHA256 / Blake2"] = WinCatalogColumn.MD5
    };

    /// <summary>
    /// Maps a raw XML element or attribute name to its semantic WinCatalog column.
    /// </summary>
    public static WinCatalogColumn MapField(string name)
    {
        var extra = ExtraAliases.TryGetValue(name.Trim(), out var column)
            ? column
            : WinCatalogColumn.Unknown;
        return extra != WinCatalogColumn.Unknown ? extra : WinCatalogProfile.MapColumn(name);
    }
}
