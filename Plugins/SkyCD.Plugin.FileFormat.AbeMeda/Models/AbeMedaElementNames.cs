using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.AbeMeda.Models;

/// <summary>
/// Name vocabulary for abeMeda/NeoFinder-family XML exports. Classification is
/// deliberately variant-tolerant: the published application offers several export
/// shapes, so volume/folder/file concepts accept documented element-name variants
/// instead of one hardcoded tag. Matching is case-insensitive.
/// </summary>
public static class AbeMedaElementNames
{
    private static readonly HashSet<string> Roots = new(StringComparer.OrdinalIgnoreCase)
    {
        "AbeMeda", "NeoFinder", "CDFinder", "CDWinder"
    };

    private static readonly HashSet<string> Volumes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Volume", "Disk", "Media", "Catalogue", "Archive", "CatalogVolume"
    };

    private static readonly HashSet<string> Folders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Folder", "Directory", "Dir"
    };

    private static readonly HashSet<string> Files = new(StringComparer.OrdinalIgnoreCase)
    {
        "File", "Item", "Track", "Song", "Photo", "Picture", "Image", "Movie", "Video", "Document"
    };

    private static readonly Dictionary<string, AbeMedaField> Fields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = AbeMedaField.Name,
            ["Title"] = AbeMedaField.Name,
            ["FileName"] = AbeMedaField.Name,
            ["VolumeName"] = AbeMedaField.Name,
            ["Path"] = AbeMedaField.Path,
            ["FullPath"] = AbeMedaField.Path,
            ["Location"] = AbeMedaField.Path,
            ["Size"] = AbeMedaField.Size,
            ["SizeBytes"] = AbeMedaField.Size,
            ["FileSize"] = AbeMedaField.Size,
            ["Length"] = AbeMedaField.Size,
            ["Kind"] = AbeMedaField.Kind,
            ["Type"] = AbeMedaField.Kind,
            ["ItemType"] = AbeMedaField.Kind,
            ["Created"] = AbeMedaField.Created,
            ["CreationDate"] = AbeMedaField.Created,
            ["DateCreated"] = AbeMedaField.Created,
            ["Modified"] = AbeMedaField.Modified,
            ["ModificationDate"] = AbeMedaField.Modified,
            ["LastModified"] = AbeMedaField.Modified,
            ["DateModified"] = AbeMedaField.Modified,
            ["Accessed"] = AbeMedaField.Accessed,
            ["LastAccessed"] = AbeMedaField.Accessed,
            ["Comment"] = AbeMedaField.Comment,
            ["Comments"] = AbeMedaField.Comment,
            ["Description"] = AbeMedaField.Comment,
            ["Notes"] = AbeMedaField.Comment,
            ["Label"] = AbeMedaField.Label,
            ["ColorLabel"] = AbeMedaField.Label,
            ["Keywords"] = AbeMedaField.Keywords,
            ["Tags"] = AbeMedaField.Keywords
        };

    private static readonly HashSet<string> ThumbnailNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Thumbnail", "Preview", "CoverArt", "Cover", "Icon", "Picture"
    };

    public static bool IsFamilyRoot(string localName) => Roots.Contains(localName);

    public static bool IsThumbnailName(string localName) => ThumbnailNames.Contains(localName);

    public static AbeMedaElementKind ClassifyByName(string localName)
    {
        if (Volumes.Contains(localName))
        {
            return AbeMedaElementKind.Volume;
        }

        if (Folders.Contains(localName))
        {
            return AbeMedaElementKind.Folder;
        }

        if (Files.Contains(localName))
        {
            return AbeMedaElementKind.File;
        }

        if (Fields.ContainsKey(localName))
        {
            return AbeMedaElementKind.Field;
        }

        return AbeMedaElementKind.Unknown;
    }

    public static AbeMedaField MapField(string name) =>
        Fields.TryGetValue(name, out var field) ? field : AbeMedaField.Unknown;
}
