using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using SkyCD.Plugin.FileFormat.DiskExplorer.Exceptions;
using SkyCD.Plugin.FileFormat.DiskExplorer.Html.Export;
using SkyCD.Plugin.FileFormat.DiskExplorer.Models;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Export;

/// <summary>
/// Writes Disk Explorer Professional XML reports with a streaming <see cref="XmlWriter"/>:
/// rows are buffered once to compute folder/file counts and then serialized without
/// building an XML DOM. Descriptions use CDATA sections split around any embedded
/// <c>]]&gt;</c> marker; everything else is XML-escaped by the writer.
/// </summary>
public static class DiskExplorerExporter
{
    private const string DefaultTitle = "SkyCD Report";

    public static async Task ExportAsync(
        Stream target,
        string? fileName,
        object? payload,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        var request = NormalizeRequest(payload);
        var title = !string.IsNullOrWhiteSpace(request.Title)
            ? request.Title.Trim()
            : !string.IsNullOrWhiteSpace(fileName)
                ? Path.GetFileNameWithoutExtension(fileName)
                : DefaultTitle;

        var rows = request.Rows.ToList();
        var tree = BuildTree(rows);
        var settings = new XmlWriterSettings
        {
            Async = true,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\n",
            OmitXmlDeclaration = false
        };

        await using var writer = XmlWriter.Create(target, settings);
        await writer.WriteStartDocumentAsync();
        await writer.WriteStartElementAsync(null, DiskExplorerNames.RootElement, null);
        await writer.WriteAttributeStringAsync(null, DiskExplorerNames.TitleAttribute, null, title);

        var total = Math.Max(1, tree.Roots.Count);
        var written = 0;
        foreach (var root in tree.Roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WriteItemAsync(writer, root, tree, cancellationToken);
            written++;
            progress?.Report(written * 100 / total);
        }

        await writer.WriteEndElementAsync();
        await writer.WriteEndDocumentAsync();
        await writer.FlushAsync();
        progress?.Report(100);
    }

    private static DiskExplorerExportRequest NormalizeRequest(object? payload)
    {
        if (payload is DiskExplorerExportRequest request)
        {
            ArgumentNullException.ThrowIfNull(request.Rows);
            return request;
        }

        return new DiskExplorerExportRequest { Rows = CoerceRows(payload) };
    }

    private static IEnumerable<Dictionary<string, object?>> CoerceRows(object? payload)
    {
        if (payload is IEnumerable<Dictionary<string, object?>> rows)
        {
            return rows;
        }

        if (payload is System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Array } array)
        {
            return array.EnumerateArray().Select(element =>
                element.ValueKind == System.Text.Json.JsonValueKind.Object
                    ? element.EnumerateObject().ToDictionary(
                        property => property.Name,
                        property => ReadJsonValue(property.Value),
                        StringComparer.OrdinalIgnoreCase)
                    : throw new DiskExplorerMalformedDocumentException("JSON array items must be objects."));
        }

        throw new DiskExplorerMalformedDocumentException("Export payload must be catalog rows or a JSON array of objects.");
    }

    private static object? ReadJsonValue(System.Text.Json.JsonElement element) => element.ValueKind switch
    {
        System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined => null,
        System.Text.Json.JsonValueKind.String => element.GetString(),
        System.Text.Json.JsonValueKind.Number => element.GetRawText(),
        System.Text.Json.JsonValueKind.True => bool.TrueString,
        System.Text.Json.JsonValueKind.False => bool.FalseString,
        _ => element.GetRawText()
    };

    private sealed record ExportNode(
        string Id,
        string? ParentId,
        string Name,
        string Type,
        long Size,
        IReadOnlyDictionary<string, object?> Properties);

    private sealed record ExportTree(IReadOnlyList<ExportNode> Roots, IReadOnlyDictionary<string, List<ExportNode>> ChildrenByParent);

    private static ExportTree BuildTree(List<Dictionary<string, object?>> rows)
    {
        var nodes = rows.Select(ReadNode).ToList();
        var byId = nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Id))
            .GroupBy(node => node.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var childrenByParent = new Dictionary<string, List<ExportNode>>(StringComparer.Ordinal);
        var roots = new List<ExportNode>();
        foreach (var node in nodes)
        {
            if (!string.IsNullOrWhiteSpace(node.ParentId) && byId.ContainsKey(node.ParentId))
            {
                if (!childrenByParent.TryGetValue(node.ParentId, out var siblings))
                {
                    siblings = [];
                    childrenByParent[node.ParentId] = siblings;
                }

                siblings.Add(node);
            }
            else
            {
                roots.Add(node);
            }
        }

        return new ExportTree(roots, childrenByParent);
    }

    private static ExportNode ReadNode(Dictionary<string, object?> row)
    {
        return new ExportNode(
            Id: ReadText(row, "id", "NodeId") ?? string.Empty,
            ParentId: ReadText(row, "parentId", "ParentId"),
            Name: ReadText(row, "name", "Name") ?? string.Empty,
            Type: (ReadText(row, "type", "Kind") ?? "file").Trim().ToLowerInvariant(),
            Size: ReadSize(row),
            Properties: ReadProperties(row));
    }

    private static string? ReadText(Dictionary<string, object?> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (row.TryGetValue(key, out var value))
            {
                var text = Convert.ToString(value, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static long ReadSize(Dictionary<string, object?> row)
    {
        var text = ReadText(row, "size", "SizeBytes");
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0L;
        }

        if (long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return Math.Max(0L, integer);
        }

        if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number))
        {
            return Math.Max(0L, (long)number);
        }

        return 0L;
    }

    private static IReadOnlyDictionary<string, object?> ReadProperties(Dictionary<string, object?> row)
    {
        foreach (var key in new[] { "properties", "Properties" })
        {
            if (row.TryGetValue(key, out var raw) && raw is IDictionary<string, object?> dict)
            {
                return new Dictionary<string, object?>(dict, StringComparer.OrdinalIgnoreCase);
            }
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    private static async Task WriteItemAsync(
        XmlWriter writer,
        ExportNode node,
        ExportTree tree,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var itemType = ResolveItemType(node);
        var children = tree.ChildrenByParent.TryGetValue(node.Id, out var direct) ? direct : [];

        await writer.WriteStartElementAsync(null, DiskExplorerNames.ItemElement, null);
        await writer.WriteAttributeStringAsync(null, DiskExplorerNames.ItemTypeAttribute, null, itemType);

        foreach (var (key, value) in node.Properties)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (key.StartsWith("DiskExplorer.ITEM.", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(value?.ToString()))
            {
                await writer.WriteAttributeStringAsync(null, key["DiskExplorer.ITEM.".Length..], null,
                    Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
            }
        }

        if (!string.IsNullOrWhiteSpace(node.Name))
        {
            await writer.WriteElementStringAsync(null, "NAME", null, node.Name);
        }

        if (itemType is not ("File" or "Archive"))
        {
            var folders = children.Count(child => child.Type is "folder" or "media");
            var files = children.Count - folders;
            await writer.WriteElementStringAsync(null, "FOLDERS", null, folders.ToString(CultureInfo.InvariantCulture));
            await writer.WriteElementStringAsync(null, "FILES", null, files.ToString(CultureInfo.InvariantCulture));
        }

        await writer.WriteElementStringAsync(null, "SIZE", null, node.Size.ToString(CultureInfo.InvariantCulture));

        var date = PropertyText(node, "Date");
        if (!string.IsNullOrWhiteSpace(date))
        {
            await writer.WriteElementStringAsync(null, "DATE", null, date);
        }

        var time = PropertyText(node, "Time");
        if (!string.IsNullOrWhiteSpace(time))
        {
            await writer.WriteElementStringAsync(null, "TIME", null, time);
        }

        var entryType = PropertyText(node, "FormatType") ?? PropertyText(node, "ExportKind");
        if (!string.IsNullOrWhiteSpace(entryType))
        {
            await writer.WriteElementStringAsync(null, "TYPE", null, entryType);
        }

        var description = PropertyText(node, "Description") ?? PropertyText(node, "Comment");
        if (!string.IsNullOrWhiteSpace(description))
        {
            await writer.WriteStartElementAsync(null, "DESC", null);
            await WriteCDataAsync(writer, description);
            await writer.WriteEndElementAsync();
        }

        foreach (var (key, value) in node.Properties
                     .Where(pair => pair.Key.StartsWith("DiskExplorer.", StringComparison.Ordinal)
                         && !pair.Key.StartsWith("DiskExplorer.ITEM.", StringComparison.Ordinal))
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(text) || !TrySplitExtensionKey(key, out var elementName))
            {
                continue;
            }

            await writer.WriteElementStringAsync(null, elementName, null, text);
        }

        foreach (var child in children)
        {
            await WriteItemAsync(writer, child, tree, cancellationToken);
        }

        await writer.WriteEndElementAsync();
    }

    private static string? PropertyText(ExportNode node, string key)
    {
        foreach (var (name, value) in node.Properties)
        {
            if (name.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                var text = Convert.ToString(value, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static string ResolveItemType(ExportNode node)
    {
        foreach (var (name, value) in node.Properties)
        {
            if (name.Equals("ExportItemType", StringComparison.OrdinalIgnoreCase))
            {
                var text = Convert.ToString(value, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(text)
                    && DiskExplorerNames.TryParseItemType(text, out var parsed))
                {
                    return DiskExplorerNames.ItemTypeToString(parsed);
                }
            }
        }

        return node.Type switch
        {
            "media" => "Volume",
            "folder" => "Folder",
            _ => "File"
        };
    }

    private static bool TrySplitExtensionKey(string key, out string elementName)
    {
        elementName = string.Empty;
        if (!key.StartsWith("DiskExplorer.", StringComparison.Ordinal))
        {
            return false;
        }

        var rest = key["DiskExplorer.".Length..];
        var dot = rest.IndexOf('.');
        elementName = dot < 0 ? rest : rest[(dot + 1)..];
        return !string.IsNullOrWhiteSpace(elementName);
    }

    private static async Task WriteCDataAsync(XmlWriter writer, string text)
    {
        // XmlWriter.WriteCData rejects embedded "]]>", so split the value across
        // adjacent CDATA sections with the marker straddling the boundary.
        var parts = text.Split("]]>", StringSplitOptions.None);
        for (var i = 0; i < parts.Length; i++)
        {
            var prefix = i > 0 ? ">" : string.Empty;
            var suffix = i < parts.Length - 1 ? "]]" : string.Empty;
            await writer.WriteCDataAsync(prefix + parts[i] + suffix);
        }
    }
}

