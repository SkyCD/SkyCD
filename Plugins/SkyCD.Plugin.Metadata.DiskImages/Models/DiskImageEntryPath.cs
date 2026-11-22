using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SkyCD.Plugin.Metadata.DiskImages.Models;

/// <summary>
/// Normalizes and validates entry paths reported by disk-image file systems so traversal segments never
/// reach the catalog or a temporary file path.
/// </summary>
internal static class DiskImageEntryPath
{
    /// <summary>
    /// Normalizes an ISO 9660 name to forward slashes, drops the <c>;version</c> suffix and upper-cases
    /// every segment, matching the catalog rows produced for optical images.
    /// </summary>
    public static string NormalizeIso9660(string? raw) =>
        string.Join('/', Split(raw).Select(static segment => StripVersion(segment).ToUpperInvariant()));

    /// <summary>
    /// Normalizes an optical image path. ISO 9660 names drop the version suffix and are upper-cased;
    /// UDF names keep their stored case.
    /// </summary>
    public static string NormalizeOptical(string? raw, bool iso9660Names) =>
        string.Join('/', Split(raw).Select(segment => iso9660Names
            ? StripVersion(segment).ToUpperInvariant()
            : segment));

    /// <summary>Returns <c>false</c> for empty, rooted, drive-qualified, or traversal paths.</summary>
    public static bool IsSafeRelativePath(string? normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (Path.IsPathRooted(normalized) || normalized.Contains(':') ||
            normalized.StartsWith('/') || normalized.StartsWith('\\'))
        {
            return false;
        }

        foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or "..")
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Normalizes a raw file-system image path to forward slashes. Returns <c>false</c> when the path is
    /// empty or contains a traversal segment or control characters.
    /// </summary>
    public static bool TryNormalizeFileSystemPath(string? raw, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var segments = Split(raw);
        if (segments.Length == 0)
        {
            return false;
        }

        var safeSegments = new List<string>(segments.Length);
        foreach (var segment in segments)
        {
            if (segment is "." or ".." || segment.Any(char.IsControl))
            {
                return false;
            }

            safeSegments.Add(segment);
        }

        normalized = string.Join('/', safeSegments);
        return normalized.Length > 0;
    }

    /// <summary>
    /// Normalizes an ISO 9660-style DAA entry path, stripping the version suffix, upper-casing segments,
    /// and rejecting absolute, traversal and control-character paths.
    /// </summary>
    public static bool TryNormalizeDaa(string? path, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var raw = path.Replace('\\', '/');
        if (raw.StartsWith('/'))
        {
            return false;
        }

        var parts = raw.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var result = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            var name = StripVersion(part);
            if (name.Length == 0 || name is "." or ".." || name.Any(char.IsControl))
            {
                return false;
            }

            result.Add(name.ToUpperInvariant());
        }

        normalized = string.Join('/', result);
        return true;
    }

    private static string[] Split(string? raw) =>
        (raw ?? string.Empty).Replace('\\', '/').Trim().Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static string StripVersion(string segment)
    {
        var versionSeparator = segment.IndexOf(';');
        return versionSeparator >= 0 ? segment[..versionSeparator] : segment;
    }
}
