using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyCD.Plugin.Metadata.Vmdk.Models;

/// <summary>
/// Normalizes and validates paths reported by a disk image file system so traversal segments never
/// reach the catalog or a temporary file path.
/// </summary>
internal static class VmdkEntryPath
{
    /// <summary>
    /// Normalizes a raw image path to forward slashes. Returns <c>false</c> when the path is empty or
    /// contains a traversal segment or control characters.
    /// </summary>
    public static bool TryNormalize(string? raw, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var candidate = raw.Replace('\\', '/').Trim();
        var segments = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries);
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

    /// <summary>Trailing file or folder segment of a normalized path.</summary>
    public static string GetLeafName(string normalizedPath)
    {
        var separator = normalizedPath.LastIndexOf('/');
        return separator >= 0 ? normalizedPath[(separator + 1)..] : normalizedPath;
    }
}
