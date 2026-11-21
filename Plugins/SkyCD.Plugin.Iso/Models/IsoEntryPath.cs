using System;
using System.Linq;

namespace SkyCD.Plugin.Iso.Models;

/// <summary>
/// Normalizes entry paths read from an ISO 9660 or UDF image and rejects paths that are not safe to
/// materialize.
/// </summary>
internal static class IsoEntryPath
{
    /// <summary>
    /// Normalizes a path to forward slashes. ISO 9660 names drop the version suffix and are upper-cased,
    /// matching the catalog rows the plugin has always produced; UDF names keep their stored case.
    /// </summary>
    public static string Normalize(string? raw, IsoImageKind kind)
    {
        var candidate = (raw ?? string.Empty).Replace('\\', '/').Trim();
        var segments = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => kind == IsoImageKind.Iso9660
                ? StripVersion(segment).ToUpperInvariant()
                : segment)
            .Where(static segment => segment.Length > 0);

        return string.Join('/', segments);
    }

    /// <summary>
    /// Returns <c>false</c> for empty, rooted, drive-qualified, or traversal paths.
    /// </summary>
    public static bool IsSafeRelativePath(string? normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (System.IO.Path.IsPathRooted(normalized) || normalized.Contains(':') ||
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

    private static string StripVersion(string segment)
    {
        var versionSeparator = segment.IndexOf(';');
        return versionSeparator >= 0 ? segment[..versionSeparator] : segment;
    }
}
