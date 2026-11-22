using System;
using System.IO;
using System.Linq;

namespace SkyCD.Plugin.Metadata.Ccd.Models;

/// <summary>
/// Normalizes entry paths read from a CloneCD image and rejects paths that are not safe to materialize.
/// </summary>
internal static class CcdEntryPath
{
    /// <summary>
    /// Normalizes a path to forward slashes, drops the ISO 9660 version suffix, and upper-cases segments,
    /// matching the catalog rows produced by the ISO image plugin.
    /// </summary>
    public static string Normalize(string? raw)
    {
        var candidate = (raw ?? string.Empty).Replace('\\', '/').Trim();
        var segments = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(static segment =>
            {
                var versionSeparator = segment.IndexOf(';');
                var withoutVersion = versionSeparator >= 0 ? segment[..versionSeparator] : segment;
                return withoutVersion.ToUpperInvariant();
            })
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
}
