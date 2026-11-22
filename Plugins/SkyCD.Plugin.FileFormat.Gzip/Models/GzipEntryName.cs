using System;
using System.Linq;

namespace SkyCD.Plugin.FileFormat.Gzip.Models;

/// <summary>
/// Resolves the catalog name of the single gzip member. The optional header name wins when it is safe;
/// otherwise the storage file name is used with its compression suffix removed.
/// </summary>
internal static class GzipEntryName
{
    private const string DefaultName = "content";

    public static string Resolve(string? originalName, string? fileName)
    {
        return TryNormalize(originalName, out var fromHeader) ? fromHeader : DeriveFromFileName(fileName);
    }

    private static bool TryNormalize(string? raw, out string normalized)
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
        if (parts.Length == 0 || parts.Any(static part => part is "." or ".."))
        {
            return false;
        }

        normalized = string.Join('/', parts);
        return true;
    }

    private static string DeriveFromFileName(string? fileName)
    {
        var leaf = (fileName ?? string.Empty).Replace('\\', '/');
        var separator = leaf.LastIndexOf('/');
        if (separator >= 0)
        {
            leaf = leaf[(separator + 1)..];
        }

        foreach (var suffix in new[] { ".gzip", ".gz" })
        {
            if (leaf.Length > suffix.Length && leaf.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return leaf[..^suffix.Length];
            }
        }

        return DefaultName;
    }
}
