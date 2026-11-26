using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.Catalog;

namespace SkyCD.App.Views;

public partial class MainWindow
{
    private static IReadOnlyList<CatalogDocument> ExtractCatalogEntries(object? payload)
    {
        if (TryExtractRows(payload, out var rows))
        {
            return BuildEntriesFromRows(rows);
        }

        if (TryExtractPathEntries(payload, out var pathEntries))
        {
            return BuildEntriesFromPaths(pathEntries);
        }

        throw new InvalidCatalogPayloadException();
    }

    private static bool TryExtractRows(object? payload, out IReadOnlyList<Dictionary<string, object?>> rows)
    {
        if (payload is JsonElement json)
        {
            if (json.ValueKind == JsonValueKind.Object)
            {
                if (json.TryGetProperty("nodes", out var nodes)) json = nodes;
                else if (json.TryGetProperty("items", out var items)) json = items;
            }
            if (json.ValueKind == JsonValueKind.Array)
            {
                rows = json.EnumerateArray().Select(item => item.EnumerateObject().ToDictionary(
                    property => property.Name, property => (object?)property.Value.Clone(), StringComparer.OrdinalIgnoreCase)).ToArray();
                return true;
            }
        }

        if (payload is IEnumerable<Dictionary<string, object?>> typedRows)
        {
            rows = typedRows.Select(row => new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase)).ToArray();
            return true;
        }

        rows = [];
        return false;
    }

    private static bool TryExtractPathEntries(object? payload, out IReadOnlyList<PathEntry> entries)
    {
        entries = [];
        if (payload is null)
        {
            return false;
        }

        var entriesProperty = payload.GetType().GetProperty("Entries", BindingFlags.Instance | BindingFlags.Public);
        if (entriesProperty?.GetValue(payload) is not System.Collections.IEnumerable rawEntries)
        {
            return false;
        }

        var result = new List<PathEntry>();
        foreach (var raw in rawEntries)
        {
            if (raw is null)
            {
                continue;
            }

            var path = raw.GetType().GetProperty("Path", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(raw) as string;
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var sizeValue = raw.GetType().GetProperty("SizeBytes", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(raw);
            var size = sizeValue is null ? 0L : Convert.ToInt64(sizeValue, CultureInfo.InvariantCulture);

            var normalizedPath = path.Trim();
            var isHttps = Uri.TryCreate(normalizedPath, UriKind.Absolute, out var uri) &&
                          uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            var domain = isHttps ? uri!.Host : null;

            var isDirectory = raw.GetType().GetProperty("IsDirectory", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(raw) is true;
            var icon = raw.GetType().GetProperty("Icon", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(raw) as string;
            result.Add(new PathEntry(normalizedPath, size, isHttps, domain, isDirectory, icon));
        }

        entries = result;
        return true;
    }

    private static IReadOnlyList<CatalogDocument> BuildEntriesFromRows(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        if (rows.Count > 0 && rows.All(row => !string.IsNullOrWhiteSpace(ReadString(row, "fullPath"))))
        {
            return BuildEntriesFromPaths(rows.Select(row => new PathEntry(
                ReadString(row, "fullPath"), ReadLong(row, "size", "sizeBytes"), false, null,
                ReadString(row, "kind").Equals("folder", StringComparison.OrdinalIgnoreCase),
                ReadString(row, "icon", "Icon"))).ToArray());
        }

        var results = new List<CatalogDocument>();
        foreach (var row in rows)
        {
            var id = ReadString(row, "id");
            if (string.IsNullOrWhiteSpace(id)) id = ReadString(row, "nodeId");
            var name = ReadString(row, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var parentId = ReadString(row, "parentId");
            if (string.IsNullOrWhiteSpace(parentId) || string.Equals(parentId, "-1", StringComparison.Ordinal))
            {
                parentId = null;
            }

            var normalizedType = ReadString(row, "type").ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalizedType)) normalizedType = ReadString(row, "kind").ToLowerInvariant();
            var documentType = normalizedType switch
            {
                "folder" => CatalogDocumentType.Folder,
                "file" => CatalogDocumentType.File,
                "media" => CatalogDocumentType.Media,
                // Collection entries (e.g. Tellico items) are catalog objects, not
                // filesystem files; they map to media rather than falling through.
                "entry" => CatalogDocumentType.Media,
                "networkfolder" => CatalogDocumentType.NetworkFolder,
                "networkresource" => CatalogDocumentType.NetworkResource,
                _ => CatalogDocumentType.Media
            };
            var status = ReadString(row, "status");
            if (string.IsNullOrWhiteSpace(status))
            {
                status = ReadString(row, "statusName");
            }

            var icon = ReadString(row, "icon");
            if (string.IsNullOrWhiteSpace(icon))
            {
                icon = ReadString(row, "Icon");
            }

            var properties = new SkyCD.Documents.Collections.PropertiesCollection();
            if (row.TryGetValue("properties", out var rawProps) && rawProps is not null)
            {
                CopyProperties(properties, rawProps);
            }
            else if (row.TryGetValue("Properties", out rawProps) && rawProps is not null)
            {
                CopyProperties(properties, rawProps);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                properties["StatusName"] = status;
            }

            if (!string.IsNullOrWhiteSpace(icon) && !properties.ContainsKey("Icon"))
            {
                properties["Icon"] = icon;
            }

            results.Add(new CatalogDocument
            {
                Id = string.IsNullOrWhiteSpace(id) ? $"doc-{Guid.NewGuid():N}" : id,
                Name = name,
                ParentId = parentId,
                Type = documentType,
                Size = ReadLong(row, "size", "sizeBytes"),
                ChildrenCount = ReadLong(row, "childrenCount"),
                ContentsState = ReadContentsState(row, properties),
                Status = string.IsNullOrWhiteSpace(status) ? null : status,
                Properties = properties
            });
        }

        return results;
    }

    private static void CopyProperties(SkyCD.Documents.Collections.PropertiesCollection target, object rawProps)
    {
        if (rawProps is JsonElement json && json.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in json.EnumerateObject())
            {
                target[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                    JsonValueKind.Number => prop.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => prop.Value.GetRawText()
                };
            }
        }
        else if (rawProps is IDictionary<string, object?> dict)
        {
            foreach (var (key, value) in dict)
            {
                if (value is not null)
                {
                    target[key] = value;
                }
            }
        }
    }

    private static IReadOnlyList<CatalogDocument> BuildEntriesFromPaths(IReadOnlyList<PathEntry> pathEntries)
    {
        var entries = new Dictionary<string, CatalogDocument>(StringComparer.Ordinal);
        var rootId = "library";
        entries[rootId] = new CatalogDocument
        {
            Id = rootId,
            Name = "Library",
            ParentId = null,
            Type = CatalogDocumentType.Folder,
            Size = 0,
            ChildrenCount = 0
        };

        foreach (var pathEntry in pathEntries
                     .OrderBy(static entry => entry.IsHttps ? 0 : 1)
                     .ThenBy(static entry => entry.Domain, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static entry => entry.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (pathEntry.IsHttps)
            {
                BuildHttpsEntry(entries, rootId, pathEntry);
                continue;
            }

            var parts = pathEntry.Path.Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            var currentParentId = rootId;
            for (var i = 0; i < parts.Length; i++)
            {
                var name = parts[i];
                var isFile = i == parts.Length - 1 && !pathEntry.IsDirectory;
                var isMediaRoot = i == 0 &&
                                  ((name.Length == 2 && char.IsLetter(name[0]) && name[1] == ':') ||
                                   (name.StartsWith('[') && name.EndsWith(']')));
                var id = CreateImportedNodeId(currentParentId, name);
                if (entries.ContainsKey(id))
                {
                    currentParentId = id;
                    continue;
                }

                var entryProperties = new SkyCD.Documents.Collections.PropertiesCollection();
                if (isFile && !string.IsNullOrWhiteSpace(pathEntry.Icon))
                {
                    entryProperties["Icon"] = pathEntry.Icon;
                }

                entries[id] = new CatalogDocument
                {
                    Id = id,
                    Name = name,
                    ParentId = currentParentId,
                    Type = isFile
                        ? CatalogDocumentType.File
                        : isMediaRoot
                            ? CatalogDocumentType.Media
                            : CatalogDocumentType.Folder,
                    Size = isFile ? pathEntry.Size : 0L,
                    ChildrenCount = 0L,
                    Properties = entryProperties
                };

                currentParentId = id;
            }
        }

        return entries.Values.ToArray();
    }

    private static void BuildHttpsEntry(IDictionary<string, CatalogDocument> entries, string rootId, PathEntry pathEntry)
    {
        if (string.IsNullOrWhiteSpace(pathEntry.Domain))
        {
            return;
        }

        var domainNodeId = CreateImportedNodeId(rootId, pathEntry.Domain);
        if (!entries.ContainsKey(domainNodeId))
        {
            entries[domainNodeId] = new CatalogDocument
            {
                Id = domainNodeId,
                Name = pathEntry.Domain,
                ParentId = rootId,
                Type = CatalogDocumentType.NetworkFolder,
                Size = 0L,
                ChildrenCount = 0L
            };
        }

        var resourceNodeId = CreateImportedNodeId(domainNodeId, pathEntry.Path);
        if (!entries.ContainsKey(resourceNodeId))
        {
            var entryProperties = new SkyCD.Documents.Collections.PropertiesCollection();
            if (!string.IsNullOrWhiteSpace(pathEntry.Icon))
            {
                entryProperties["Icon"] = pathEntry.Icon;
            }

            entries[resourceNodeId] = new CatalogDocument
            {
                Id = resourceNodeId,
                Name = pathEntry.Path,
                ParentId = domainNodeId,
                Type = CatalogDocumentType.NetworkResource,
                Size = pathEntry.Size,
                ChildrenCount = 0L,
                Properties = entryProperties
            };
        }
    }

    private static CatalogContentsState ReadContentsState(
        IReadOnlyDictionary<string, object?> row,
        SkyCD.Documents.Collections.PropertiesCollection properties)
    {
        var raw = ReadString(row, "contentsState", "ContentsState");
        if (string.IsNullOrWhiteSpace(raw) &&
            properties.TryGetValue("ContentsState", out var propertyValue))
        {
            raw = propertyValue switch
            {
                JsonElement json when json.ValueKind == JsonValueKind.String => json.GetString() ?? string.Empty,
                JsonElement json => json.ToString(),
                _ => propertyValue?.ToString() ?? string.Empty
            };
        }

        return Enum.TryParse<CatalogContentsState>(raw, ignoreCase: true, out var parsed)
            ? parsed
            : CatalogContentsState.Known;
    }

    private static string ReadString(IReadOnlyDictionary<string, object?> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (row.TryGetValue(key, out var value) && value is not null)
            {
                var str = value is JsonElement { ValueKind: JsonValueKind.Null } ? string.Empty : value.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(str))
                {
                    return str;
                }
            }
        }

        return string.Empty;
    }

    private static long ReadLong(IReadOnlyDictionary<string, object?> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!row.TryGetValue(key, out var value) || value is null)
            {
                continue;
            }

            if (value is long direct)
            {
                return direct;
            }

            if (long.TryParse(value.ToString(), out var parsed))
            {
                return parsed;
            }
        }

        return 0L;
    }

    private static string CreateImportedNodeId(string parentId, string name)
    {
        // Catalog paths and URLs can exceed Couchbase's document-ID limit.
        // Hash each parent/name pair so IDs stay bounded at every tree depth.
        var identity = parentId + "/" + name.ToLowerInvariant();
        return "import-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    private sealed record PathEntry(string Path, long Size, bool IsHttps, string? Domain, bool IsDirectory, string? Icon = null);
}
