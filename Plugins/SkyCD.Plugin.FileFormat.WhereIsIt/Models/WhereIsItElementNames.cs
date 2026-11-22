using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.WhereIsIt.Models;

/// <summary>
/// Name vocabulary for Where Is It? XML reports, following the Librer reference
/// mapping of the "List of all cataloged files and folders on selected disks"
/// export. Field elements match case-sensitively like the reference parser;
/// <c>ItemType</c> values match case-sensitively as well.
/// </summary>
public static class WhereIsItElementNames
{
    public const string RootElement = "REPORT";
    public const string ItemElement = "ITEM";
    public const string TitleAttribute = "Title";
    public const string ItemTypeAttribute = "ItemType";

    public const string DemoMarker = "*** DEMO ***";

    private static readonly Dictionary<string, WhereIsItField> Fields =
        new(StringComparer.Ordinal)
        {
            ["NAME"] = WhereIsItField.Name,
            ["EXT"] = WhereIsItField.Extension,
            ["SIZE"] = WhereIsItField.Size,
            ["DATE"] = WhereIsItField.Date,
            ["TIME"] = WhereIsItField.Time,
            ["PATH"] = WhereIsItField.Path,
            ["DISK_NAME"] = WhereIsItField.DiskName,
            ["DISK_TYPE"] = WhereIsItField.DiskType,
            ["DISK_NUM"] = WhereIsItField.DiskNumber,
            ["DISK_LOCATION"] = WhereIsItField.DiskLocation,
            ["CRC"] = WhereIsItField.Crc,
            ["CATEGORY"] = WhereIsItField.Category,
            ["FLAG"] = WhereIsItField.Flag,
            ["DESCRIPTION"] = WhereIsItField.Description
        };

    private static readonly HashSet<string> Markers = new(StringComparer.OrdinalIgnoreCase)
    {
        "DISK_NAME", "DISK_TYPE", "DISK_NUM", "DISK_LOCATION",
        "EXT", "CRC", "CATEGORY", "FLAG"
    };

    public static bool IsReportMarker(string localName) => Markers.Contains(localName);

    public static WhereIsItField MapField(string name) =>
        Fields.TryGetValue(name, out var field) ? field : WhereIsItField.Unknown;

    public static WhereIsItElementKind ClassifyItemType(string? value) => value switch
    {
        "File" => WhereIsItElementKind.File,
        "Folder" => WhereIsItElementKind.Folder,
        "Disk" => WhereIsItElementKind.Disk,
        _ => WhereIsItElementKind.Unknown
    };

    public static bool IsDemoValue(string? value) =>
        !string.IsNullOrEmpty(value) && value.Contains(DemoMarker, StringComparison.Ordinal);
}
