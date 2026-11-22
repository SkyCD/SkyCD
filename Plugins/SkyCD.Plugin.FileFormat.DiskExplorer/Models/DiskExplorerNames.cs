using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Models;

/// <summary>
/// Name vocabulary for the Disk Explorer Professional XML report profile.
/// `ItemType` matching is case-insensitive on import for resilience while export
/// always writes the canonical case-sensitive spelling.
/// </summary>
public static class DiskExplorerNames
{
    public const string RootElement = "REPORT";
    public const string ItemElement = "ITEM";
    public const string TitleAttribute = "Title";
    public const string ItemTypeAttribute = "ItemType";

    private static readonly Dictionary<string, DiskExplorerElementKind> ItemTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Volume"] = DiskExplorerElementKind.Volume,
            ["CategoryFolder"] = DiskExplorerElementKind.CategoryFolder,
            ["Folder"] = DiskExplorerElementKind.Folder,
            ["Archive"] = DiskExplorerElementKind.Archive,
            ["File"] = DiskExplorerElementKind.File
        };

    private static readonly HashSet<string> Thumbnails = new(StringComparer.OrdinalIgnoreCase)
    {
        "Thumbnail", "Preview", "CoverArt", "Cover", "Icon", "Picture"
    };

    public static bool IsThumbnailName(string localName) => Thumbnails.Contains(localName);

    private static readonly Dictionary<string, DiskExplorerField> Fields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["NAME"] = DiskExplorerField.Name,
            ["FILES"] = DiskExplorerField.Files,
            ["FOLDERS"] = DiskExplorerField.Folders,
            ["SIZE"] = DiskExplorerField.Size,
            ["DATE"] = DiskExplorerField.Date,
            ["TIME"] = DiskExplorerField.Time,
            ["TYPE"] = DiskExplorerField.Type,
            ["DESC"] = DiskExplorerField.Description
        };

    public static bool TryParseItemType(string? value, out DiskExplorerElementKind kind)
    {
        if (!string.IsNullOrWhiteSpace(value) && ItemTypes.TryGetValue(value.Trim(), out kind))
        {
            return true;
        }

        kind = DiskExplorerElementKind.Unknown;
        return false;
    }

    public static string ItemTypeToString(DiskExplorerElementKind kind) => kind switch
    {
        DiskExplorerElementKind.Volume => "Volume",
        DiskExplorerElementKind.CategoryFolder => "CategoryFolder",
        DiskExplorerElementKind.Folder => "Folder",
        DiskExplorerElementKind.Archive => "Archive",
        DiskExplorerElementKind.File => "File",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static DiskExplorerField MapField(string name) =>
        Fields.TryGetValue(name, out var field) ? field : DiskExplorerField.Unknown;
}
