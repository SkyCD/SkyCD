using System.Collections.Generic;
using System.Linq;
using SkyCD.Plugin.FileFormat.LegacySkyCD.Models;

namespace SkyCD.Plugin.FileFormat.LegacySkyCD.Parsing;

/// <summary>
/// Builds legacy path entries from flat catalog rows, reconstructing the parent chain into
/// backslash-separated paths so path-only legacy formats keep their hierarchy.
/// </summary>
internal static class LegacyRowPaths
{
    public static List<LegacyRowEntry> Build(IEnumerable<Dictionary<string, object?>> rows)
    {
        var list = rows.ToList();
        var byId = new Dictionary<string, Dictionary<string, object?>>(System.StringComparer.Ordinal);
        foreach (var row in list)
        {
            var id = ReadString(row, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                id = ReadString(row, "nodeId");
            }

            if (!string.IsNullOrWhiteSpace(id))
            {
                byId.TryAdd(id, row);
            }
        }

        var paths = new Dictionary<string, string>(System.StringComparer.Ordinal);
        string Resolve(Dictionary<string, object?> row, int depth)
        {
            var id = ReadString(row, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                id = ReadString(row, "nodeId");
            }

            if (depth < 64 && !string.IsNullOrWhiteSpace(id) && paths.TryGetValue(id, out var cached))
            {
                return cached;
            }

            var name = ReadString(row, "name").Trim();
            var parentId = ReadString(row, "parentId");
            var path = name;
            if (depth < 64 && !string.IsNullOrWhiteSpace(parentId) && byId.TryGetValue(parentId, out var parent))
            {
                var parentPath = Resolve(parent, depth + 1);
                path = parentPath.EndsWith('\\') ? parentPath + name : $"{parentPath}\\{name}";
            }

            if (!string.IsNullOrWhiteSpace(id))
            {
                paths[id] = path;
            }

            return path;
        }

        var result = new List<LegacyRowEntry>(list.Count);
        foreach (var row in list)
        {
            var kind = ReadString(row, "kind");
            if (string.IsNullOrWhiteSpace(kind))
            {
                kind = ReadString(row, "type");
            }

            var isFolder = kind.Equals("folder", System.StringComparison.OrdinalIgnoreCase)
                || kind.Equals("media", System.StringComparison.OrdinalIgnoreCase);
            var path = Resolve(row, 0);
            if (isFolder && !path.EndsWith('\\'))
            {
                path += "\\";
            }

            long? size = isFolder ? null : ReadSize(row);
            result.Add(new LegacyRowEntry(path, size, isFolder));
        }

        return result;
    }

    private static long? ReadSize(Dictionary<string, object?> row)
    {
        foreach (var key in new[] { "size", "sizeBytes" })
        {
            if (!TryGet(row, key, out var value) || value is null)
            {
                continue;
            }

            if (long.TryParse(value.ToString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static string ReadString(Dictionary<string, object?> row, string key) =>
        TryGet(row, key, out var value) ? value?.ToString() ?? string.Empty : string.Empty;

    private static bool TryGet(Dictionary<string, object?> row, string key, out object? value)
    {
        if (row.TryGetValue(key, out value))
        {
            return true;
        }

        foreach (var (candidate, candidateValue) in row)
        {
            if (candidate.Equals(key, System.StringComparison.OrdinalIgnoreCase))
            {
                value = candidateValue;
                return true;
            }
        }

        value = null;
        return false;
    }
}

internal readonly record struct LegacyRowEntry(string Path, long? SizeBytes, bool IsDirectory);
