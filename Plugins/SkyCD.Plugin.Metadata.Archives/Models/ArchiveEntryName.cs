using System;

namespace SkyCD.Plugin.Metadata.Archives.Models;

/// <summary>
/// Resolves the catalog name of a single-stream entry. Streams without a stored name use the storage file
/// name with one of the supplied compression suffixes removed.
/// </summary>
internal static class ArchiveEntryName
{
    private const string DefaultName = "content";

    /// <summary>Derives the entry name from <paramref name="fileName"/> by removing the first matching suffix.</summary>
    public static string DeriveFromFileName(string? fileName, params string[] suffixes)
    {
        var leaf = (fileName ?? string.Empty).Replace('\\', '/');
        var separator = leaf.LastIndexOf('/');
        if (separator >= 0)
        {
            leaf = leaf[(separator + 1)..];
        }

        foreach (var suffix in suffixes)
        {
            if (leaf.Length > suffix.Length && leaf.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return leaf[..^suffix.Length];
            }
        }

        return DefaultName;
    }

    /// <summary>
    /// Normalizes a name stored in a stream header. Returns <c>false</c> when it is empty, drive-qualified,
    /// or contains traversal segments.
    /// </summary>
    public static bool TryNormalize(string? raw, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var candidate = raw.Replace('\\', '/').Trim().TrimStart('/');
        if (candidate.Length == 0 || candidate.Contains(':'))
        {
            return false;
        }

        var parts = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (part is "." or "..")
            {
                return false;
            }
        }

        normalized = string.Join('/', parts);
        return true;
    }
}
