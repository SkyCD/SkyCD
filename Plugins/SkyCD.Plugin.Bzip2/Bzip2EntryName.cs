using System;

namespace SkyCD.Plugin.Bzip2;

/// <summary>
/// Resolves the catalog name of the single bzip2 stream. bzip2 stores no file name, so the storage name
/// is used with its compression suffix removed.
/// </summary>
internal static class Bzip2EntryName
{
    private const string DefaultName = "content";

    public static string Resolve(string? fileName)
    {
        var leaf = (fileName ?? string.Empty).Replace('\\', '/');
        var separator = leaf.LastIndexOf('/');
        if (separator >= 0)
        {
            leaf = leaf[(separator + 1)..];
        }

        foreach (var suffix in new[] { ".tbz2", ".tbz", ".bz2", ".bz" })
        {
            if (leaf.Length > suffix.Length && leaf.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return leaf[..^suffix.Length];
            }
        }

        return DefaultName;
    }
}
