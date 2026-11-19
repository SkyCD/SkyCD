using System;
using System.Collections.Generic;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Decides which catalog properties are user annotations and which are facts written by the scanners and
/// the staging merger. Extraction results are stored in the media cache and never written back into entry
/// properties, so a refresh cannot overwrite comments, tags, ratings or custom properties.
/// </summary>
public static class MediaAnnotationPolicy
{
    /// <summary>Property keys written by catalog scanning, media capture and scan publication.</summary>
    private static readonly HashSet<string> ScanFactKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Name",
        "Size",
        "LastWriteTime",
        "LastAccessTime",
        "CreationTime",
        "IsReadOnly",
        "Attributes",
        "FullName",
        "RealDirectoryFullName",
        "RealDirectoryName",
        "Title",
        "VolumeLabel",
        "VolumeSerialNumber",
        "RootDirectory",
        "AvailableFreeSpace",
        "TotalFreeSpace",
        "TotalSize",
        "DriveType",
        "DriveFormat",
        "ContentType",
        "Icon",
        "ContentsState",
        "ScanErrorSummary"
    };

    /// <summary>Property keys that are always treated as user annotations.</summary>
    private static readonly HashSet<string> KnownUserKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Comments",
        "Tags",
        "Rating",
        "Status",
        "LocationId"
    };

    public static bool IsScanFact(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return ScanFactKeys.Contains(key);
    }

    /// <summary>
    /// A property is a user annotation when it is a known user key or was not written by scanning, because
    /// only the scanner, the scan publisher and the user write entry properties.
    /// </summary>
    public static bool IsUserAnnotation(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return KnownUserKeys.Contains(key) || !ScanFactKeys.Contains(key);
    }
}
