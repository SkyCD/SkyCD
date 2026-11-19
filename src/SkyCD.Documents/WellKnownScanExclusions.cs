using System;
using System.Collections.Generic;

namespace SkyCD.Documents;

/// <summary>
/// Well-known entry names that catalog scans can skip via toggles, plus the platform-aware
/// default exclusion rules seeded into the managed scan rule list.
/// </summary>
public static class WellKnownScanExclusions
{
    private static StringComparer NameComparer { get; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Recycle bin / trash folder names, matched on every platform.</summary>
    public static IReadOnlySet<string> RecycleBinNames { get; } = new HashSet<string>(NameComparer)
    {
        "$RECYCLE.BIN",
        "RECYCLER",
        ".Trash",
        ".Trash-1000",
        ".Trashes"
    };

    /// <summary>Well-known system folder names, matched on every platform.</summary>
    public static IReadOnlySet<string> SystemFolderNames { get; } = new HashSet<string>(NameComparer)
    {
        "System Volume Information",
        ".Spotlight-V100",
        ".fseventsd",
        ".TemporaryItems",
        "lost+found"
    };

    /// <summary>Well-known system file names, matched on every platform.</summary>
    public static IReadOnlySet<string> SystemFileNames { get; } = new HashSet<string>(NameComparer)
    {
        "Thumbs.db",
        "desktop.ini",
        "pagefile.sys",
        "hiberfil.sys",
        "swapfile.sys",
        ".DS_Store"
    };

    /// <summary>
    /// Default exclusion rules for the current platform. Entry types are explicit so a file-only
    /// rule never excludes a folder with the same name (and the other way around).
    /// </summary>
    public static IReadOnlyList<ScanExclusionRuleDocument> GetDefaultExclusionRules()
    {
        if (OperatingSystem.IsWindows())
        {
            return
            [
                FolderRule("$RECYCLE.BIN"),
                FolderRule("RECYCLER"),
                FolderRule("System Volume Information"),
                FileRule("Thumbs.db"),
                FileRule("desktop.ini"),
                FileRule("pagefile.sys"),
                FileRule("hiberfil.sys"),
                FileRule("swapfile.sys")
            ];
        }

        if (OperatingSystem.IsLinux())
        {
            return
            [
                FolderRule(".Trash"),
                FolderRule(".Trash-1000"),
                FolderRule("lost+found")
            ];
        }

        if (OperatingSystem.IsMacOS())
        {
            return
            [
                FolderRule(".Trashes"),
                FolderRule(".Spotlight-V100"),
                FolderRule(".fseventsd"),
                FolderRule(".TemporaryItems"),
                FileRule(".DS_Store")
            ];
        }

        return [];
    }

    private static ScanExclusionRuleDocument FileRule(string pattern) => new()
    {
        Pattern = pattern,
        EntryType = "File",
        IsInclude = false
    };

    private static ScanExclusionRuleDocument FolderRule(string pattern) => new()
    {
        Pattern = pattern,
        EntryType = "Folder",
        IsInclude = false
    };
}
