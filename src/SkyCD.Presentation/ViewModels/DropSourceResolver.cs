using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Presentation.Scanning;

namespace SkyCD.Presentation.ViewModels;

/// <summary>A dropped file-system entry as reported by the platform, before it is classified.</summary>
public readonly record struct DroppedPath(string Path, bool IsDirectory);

/// <summary>
/// Maps an OS drag-and-drop payload (folders, drive roots, network shares and http(s) text) onto
/// Add To List sources. The UI layer extracts the raw payload; this type stays free of Avalonia so
/// the mapping is unit-testable. Path separators are handled platform-independently because a
/// dropped path comes from the platform that produced it.
/// </summary>
public static class DropSourceResolver
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static readonly HashSet<string> UrlSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https", "ftp", "ftps", "sftp", "smb", "webdav", "webdavs"
    };

    public static IReadOnlyList<DroppedSource> Resolve(
        IEnumerable<DroppedPath> droppedPaths,
        string? droppedText,
        IReadOnlyList<MediaDriveInfo>? availableDrives)
    {
        ArgumentNullException.ThrowIfNull(droppedPaths);
        var drives = availableDrives ?? [];
        var results = new List<DroppedSource>();
        var seen = new HashSet<string>(PathComparer);

        foreach (var dropped in droppedPaths)
        {
            if (!dropped.IsDirectory)
            {
                continue;
            }

            if (ClassifyPath(dropped.Path, drives) is not { } source)
            {
                continue;
            }

            if (seen.Add(source.Mode + "|" + source.Value))
            {
                results.Add(source);
            }
        }

        if (results.Count == 0 && TryResolveUrl(droppedText) is { } url)
        {
            results.Add(url);
        }

        return results;
    }

    /// <summary>Returns an Internet source for an absolute URL with a supported remote scheme.</summary>
    public static DroppedSource? TryResolveUrl(string? text)
    {
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value) || value.Any(char.IsControl))
        {
            return null;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !UrlSchemes.Contains(uri.Scheme))
        {
            return null;
        }

        // SMB is only offered by the Internet tab on Windows.
        if (uri.Scheme.Equals("smb", StringComparison.OrdinalIgnoreCase) && !OperatingSystem.IsWindows())
        {
            return null;
        }

        return new DroppedSource(AddToListSourceMode.Internet, value, null);
    }

    private static DroppedSource? ClassifyPath(string? path, IReadOnlyList<MediaDriveInfo> drives)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalized = TrimTrailingSeparators(path.Trim());
        if (normalized.Length == 0)
        {
            return null;
        }

        if (FindDrive(normalized, drives) is { } drive)
        {
            return new DroppedSource(
                AddToListSourceMode.Media,
                drive.RootDirectory,
                string.IsNullOrWhiteSpace(drive.VolumeLabel) ? null : drive.VolumeLabel);
        }

        var name = GetLeafName(normalized);
        return new DroppedSource(
            AddToListSourceMode.Folder,
            normalized,
            string.IsNullOrWhiteSpace(name) ? normalized : name);
    }

    private static MediaDriveInfo? FindDrive(string path, IReadOnlyList<MediaDriveInfo> drives)
    {
        var candidate = TrimSeparatorsForComparison(path);
        if (candidate.Length == 0)
        {
            return null;
        }

        foreach (var drive in drives)
        {
            if (string.Equals(candidate, TrimSeparatorsForComparison(drive.RootDirectory), PathComparison) ||
                string.Equals(candidate, TrimSeparatorsForComparison(drive.Name), PathComparison))
            {
                return drive;
            }
        }

        return null;
    }

    private static string TrimSeparatorsForComparison(string path) =>
        path.Trim().TrimEnd('/', '\\');

    private static string TrimTrailingSeparators(string path)
    {
        if (path.Length <= 1)
        {
            return path;
        }

        var trimmed = path.TrimEnd('/', '\\');
        if (trimmed.Length == 0)
        {
            // A bare root ("/" or "\") keeps its separator.
            return path;
        }

        // A Windows drive root ("C:\") keeps its separator; only nested folders are trimmed.
        return trimmed.EndsWith(':') ? path : trimmed;
    }

    private static string GetLeafName(string path)
    {
        var trimmed = path.TrimEnd('/', '\\');
        var index = trimmed.LastIndexOfAny(['/', '\\']);
        var name = index < 0 ? trimmed : trimmed[(index + 1)..];
        return name.Length == 0 ? trimmed : name;
    }
}
