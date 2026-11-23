using System;
using System.Collections.Generic;
using System.IO;

namespace SkyCD.PackagePaths;

/// <summary>Per-format deviations from the default package entry path rules.</summary>
internal enum PackageEntryPathRules
{
    /// <summary>Treats "." and ".." segments as unsafe and otherwise keeps every segment.</summary>
    None = 0,

    /// <summary>Drops "." segments and only reports ".." traversal as unsafe.</summary>
    SkipCurrentDirectorySegments = 1,

    /// <summary>Treats segments containing control characters as unsafe in addition to "." and "..".</summary>
    RejectControlCharacters = 2
}

/// <summary>
/// Normalizes and validates package and archive entry paths so extraction cannot escape the target directory.
/// </summary>
/// <remarks>
/// This file is linked into the host (SkyCD.Core) and the archive and disk image plugins, so package and archive
/// entry rules cannot drift.
/// </remarks>
internal static class PackageEntryPath
{
    /// <summary>Rules for plugin package entries: drop "." segments and reject control characters.</summary>
    public const PackageEntryPathRules PackageRules =
        PackageEntryPathRules.SkipCurrentDirectorySegments | PackageEntryPathRules.RejectControlCharacters;

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>
    /// Normalizes a raw entry path to forward slashes. Returns <c>false</c> when the entry has no path.
    /// <paramref name="safe"/> is <c>false</c> when the path is absolute, drive-qualified, or contains traversal
    /// segments.
    /// </summary>
    public static bool TryNormalize(string? raw, out string normalized, out bool safe,
        PackageEntryPathRules rules = PackageEntryPathRules.None)
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
            if (segment == "." && rules.HasFlag(PackageEntryPathRules.SkipCurrentDirectorySegments))
            {
                continue;
            }

            if (segment is "." or ".."
                || (rules.HasFlag(PackageEntryPathRules.RejectControlCharacters) && HasControlCharacter(segment)))
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

    /// <summary>True only when the raw path normalizes and is safe.</summary>
    public static bool TryNormalizeSafe(string? raw, out string normalized,
        PackageEntryPathRules rules = PackageEntryPathRules.None) =>
        TryNormalize(raw, out normalized, out var safe, rules) && safe;

    /// <summary>
    /// Resolves a normalized, safe entry path under <paramref name="root"/> and verifies containment.
    /// <paramref name="suppressErrors"/> returns <c>false</c> instead of propagating invalid path errors.
    /// </summary>
    public static bool TryResolveUnderRoot(string root, string normalized, out string fullPath,
        bool suppressErrors = false)
    {
        fullPath = string.Empty;
        try
        {
            fullPath = Path.GetFullPath(Path.Combine(root, normalized));
        }
        catch (Exception exception) when (suppressErrors
                                          && exception is ArgumentException or NotSupportedException
                                              or PathTooLongException)
        {
            return false;
        }

        var rootWithSeparator = Path.EndsInDirectorySeparator(root)
            ? root
            : root + Path.DirectorySeparatorChar;

        return fullPath.StartsWith(rootWithSeparator, PathComparison)
               || string.Equals(fullPath, root, PathComparison);
    }

    private static bool HasControlCharacter(string segment)
    {
        foreach (var character in segment)
        {
            if (char.IsControl(character))
            {
                return true;
            }
        }

        return false;
    }
}
