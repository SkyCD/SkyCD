using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.DiskExplorer.Ded;

/// <summary>
/// Object kinds stored in the first field of a DED catalog record.
/// Unknown wire values are retained through <see cref="DedCatalogObject.RawKind"/>
/// and opaque record bytes.
/// </summary>
public enum DedObjectKind
{
    Unknown = 0,
    Database = 1,
    CategoryFolder = 2,
    Volume = 3,
    Folder = 4,
    Archive = 5,
    File = 6
}

/// <summary>
/// Case-insensitive textual names used in catalog rows and reports.
/// </summary>
public static class DedObjectKinds
{
    public const string Database = "database";
    public const string CategoryFolder = "category-folder";
    public const string Volume = "volume";
    public const string Folder = "folder";
    public const string Archive = "archive";
    public const string File = "file";
    public const string Unknown = "unknown";

    private static readonly Dictionary<string, DedObjectKind> ByName =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [Database] = DedObjectKind.Database,
            [CategoryFolder] = DedObjectKind.CategoryFolder,
            [Volume] = DedObjectKind.Volume,
            [Folder] = DedObjectKind.Folder,
            [Archive] = DedObjectKind.Archive,
            [File] = DedObjectKind.File,
            [Unknown] = DedObjectKind.Unknown
        };

    public static string ToText(DedObjectKind kind) => kind switch
    {
        DedObjectKind.Database => Database,
        DedObjectKind.CategoryFolder => CategoryFolder,
        DedObjectKind.Volume => Volume,
        DedObjectKind.Folder => Folder,
        DedObjectKind.Archive => Archive,
        DedObjectKind.File => File,
        _ => Unknown
    };

    public static bool TryParse(string? text, out DedObjectKind kind)
    {
        if (!string.IsNullOrWhiteSpace(text) && ByName.TryGetValue(text.Trim(), out kind))
        {
            return true;
        }

        kind = DedObjectKind.Unknown;
        return false;
    }

    public static bool IsKnownValue(uint rawKind) =>
        rawKind is >= (uint)DedObjectKind.Database and <= (uint)DedObjectKind.File;
}
