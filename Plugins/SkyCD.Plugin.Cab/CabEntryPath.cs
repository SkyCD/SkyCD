using System;
using System.Collections.Generic;
using System.IO;

namespace SkyCD.Plugin.Cab;

/// <summary>
/// Normalizes and validates cabinet member paths so extraction cannot escape the temporary directory.
/// </summary>
internal static class CabEntryPath
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>
    /// Normalizes a raw cabinet member name to forward slashes. Returns <c>false</c> when the name has no path.
    /// <paramref name="safe"/> is <c>false</c> when the path is absolute, drive-qualified, or contains traversal segments.
    /// </summary>
    public static bool TryNormalize(string? raw, out string normalized, out bool safe)
    {
        normalized = string.Empty;
        safe = false;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var candidate = raw.Replace('\\', '/').Trim();
        if (candidate.Length == 0)
        {
            return false;
        }

        if (Path.IsPathRooted(candidate) || candidate.StartsWith('/') || candidate.Contains(':'))
        {
            normalized = candidate.Trim('/');
            return true;
        }

        var segments = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        var safeSegments = new List<string>(segments.Length);
        foreach (var segment in segments)
        {
            if (segment is "." or "..")
            {
                normalized = string.Join('/', segments);
                return true;
            }

            safeSegments.Add(segment);
        }

        normalized = string.Join('/', safeSegments);
        safe = normalized.Length > 0;
        return true;
    }

    /// <summary>
    /// Resolves a normalized, safe entry path under <paramref name="root"/> and verifies containment.
    /// </summary>
    public static bool TryResolveUnderRoot(string root, string normalized, out string fullPath)
    {
        fullPath = Path.GetFullPath(Path.Combine(root, normalized));
        var rootWithSeparator = Path.EndsInDirectorySeparator(root)
            ? root
            : root + Path.DirectorySeparatorChar;

        return fullPath.StartsWith(rootWithSeparator, PathComparison)
               || string.Equals(fullPath, root, PathComparison);
    }
}
