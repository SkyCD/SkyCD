using System;
using System.Collections.Generic;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Maps <see cref="ScanFileSystemEntry"/> items read from an SMB share onto the catalog's
/// <see cref="RemoteEntry"/> (#626). Names, sizes and UTC timestamps follow the local scan;
/// reparse points and directory symlinks carry their resolved target as the visited identity
/// so the shared cycle logic treats them exactly like SFTP symlinks.
/// </summary>
public static class SmbRemoteListingMapper
{
    public static IReadOnlyList<RemoteEntry> Map(
        IEnumerable<ScanFileSystemEntry?>? entries,
        string directoryPath,
        string host)
    {
        var mapped = new List<RemoteEntry>();
        if (entries is null)
        {
            return mapped;
        }

        var directory = FtpPath.Normalize(directoryPath);
        foreach (var entry in entries)
        {
            var item = MapItem(entry, directory, host);
            if (item is not null)
            {
                mapped.Add(item);
            }
        }

        return mapped;
    }

    /// <summary>
    /// Projects one filesystem entry. Returns null for names the catalog cannot represent
    /// (empty, dot entries, or names containing a separator). Boundary whitespace is kept:
    /// Windows names may legitimately contain characters like U+3000.
    /// </summary>
    public static RemoteEntry? MapItem(ScanFileSystemEntry? entry, string directoryPath, string host)
    {
        if (entry is null)
        {
            return null;
        }

        var name = entry.Name ?? string.Empty;
        if (!IsCatalogName(name))
        {
            return null;
        }

        var directory = FtpPath.Normalize(directoryPath);
        var isDirectory = entry.IsDirectory;
        var size = isDirectory ? 0 : Math.Max(0, entry.Length);

        return new RemoteEntry(
            name,
            FtpPath.Combine(directory, name),
            directory,
            isDirectory,
            size,
            null,
            ModifiedUtc(entry),
            SmbPath.ToRemotePath(entry.VisitedIdentity, host));
    }

    /// <summary>The entry timestamp normalized to UTC, or null when none was reported.</summary>
    internal static DateTime? ModifiedUtc(ScanFileSystemEntry entry)
    {
        var modified = entry.LastWriteTime;
        if (modified == DateTime.MinValue)
        {
            return null;
        }

        return modified.Kind switch
        {
            DateTimeKind.Utc => modified,
            DateTimeKind.Local => modified.ToUniversalTime(),
            _ => DateTime.SpecifyKind(modified, DateTimeKind.Utc),
        };
    }

    private static bool IsCatalogName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name is not "." and not ".." &&
        name.IndexOfAny(['/', '\\', '\0']) < 0;
}
