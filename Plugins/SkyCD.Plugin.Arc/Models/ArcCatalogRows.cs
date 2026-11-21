using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyCD.Plugin.Arc.Models;

/// <summary>Turns ARC headers into the folder and file rows the host imports as catalog hierarchy.</summary>
internal static class ArcCatalogRows
{
    /// <summary>
    /// Projects a listing into rows. Missing parent folders of nested paths are created, reserved settings
    /// headers are skipped, and every file reports its uncompressed size in bytes.
    /// </summary>
    public static List<Dictionary<string, object?>> Project(IEnumerable<ArcEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var rows = new List<Dictionary<string, object?>>();
        var seenDirectories = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (entry.Method == ArcMethod.Reserved)
            {
                continue;
            }

            var normalizedPath = NormalizeDisplayPath(entry.Name).TrimEnd('/');
            if (normalizedPath.Length == 0)
            {
                continue;
            }

            var parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            var modifiedUtc = entry.ModifiedUtc?.ToString("O");
            for (var index = 0; index < parts.Length - 1; index++)
            {
                var directoryPath = string.Join("/", parts.Take(index + 1));
                if (!seenDirectories.Add(directoryPath))
                {
                    continue;
                }

                rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["kind"] = "folder",
                    ["fullPath"] = directoryPath,
                    ["name"] = parts[index],
                    ["sizeBytes"] = "0",
                    ["modifiedUtc"] = modifiedUtc
                });
            }

            var leafName = parts[^1];
            if (entry.IsDirectory)
            {
                if (seenDirectories.Add(normalizedPath))
                {
                    rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["kind"] = "folder",
                        ["fullPath"] = normalizedPath,
                        ["name"] = leafName,
                        ["sizeBytes"] = "0",
                        ["modifiedUtc"] = modifiedUtc
                    });
                }
            }
            else
            {
                rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["kind"] = "file",
                    ["fullPath"] = normalizedPath,
                    ["name"] = leafName,
                    ["sizeBytes"] = SafeSize(entry).ToString(),
                    ["modifiedUtc"] = modifiedUtc
                });
            }
        }

        return rows
            .OrderBy(row => row["fullPath"]?.ToString(), StringComparer.Ordinal)
            .ThenBy(row => row["kind"]?.ToString(), StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Rewrites a raw entry name as a portable path with forward slashes. Names are kept verbatim otherwise so
    /// the catalog shows exactly what the archive contained; extraction applies the safety checks separately.
    /// </summary>
    public static string NormalizeDisplayPath(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Replace('\\', '/').Trim();

    private static long SafeSize(ArcEntry entry) => Math.Max(entry.UncompressedSize, 0);
}
