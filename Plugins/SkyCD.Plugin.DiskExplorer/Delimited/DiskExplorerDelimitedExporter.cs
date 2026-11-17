using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.DiskExplorer.Delimited.Exceptions;

namespace SkyCD.Plugin.DiskExplorer.Delimited;

/// <summary>
/// Writes Disk Explorer delimited-text reports from catalog rows. Rows are
/// buffered once to rebuild parent chains and relative paths, then serialized
/// in encounter order with the requested (or default) column layout.
/// </summary>
public static class DiskExplorerDelimitedExporter
{
    public static async Task ExportAsync(
        Stream target,
        string? fileName,
        object? payload,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        var request = NormalizeRequest(payload);

        var columns = request.Columns is { Count: > 0 }
            ? request.Columns.ToList()
            : DiskExplorerDelimitedColumns.DefaultExportColumns.ToList();
        var encoding = request.Encoding == DiskExplorerDelimitedEncoding.Windows1252
            ? CodePagesEncodingProvider.Instance.GetEncoding(
                1252,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback)!
            : new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);
        var newLine = request.NewLine
            ?? (request.Encoding == DiskExplorerDelimitedEncoding.Windows1252 ? "\r\n" : "\n");

        var rows = request.Rows.ToList();
        var nodes = IndexRows(rows);

        await using var writer = new StreamWriter(target, encoding, bufferSize: 4096, leaveOpen: true)
        {
            NewLine = newLine
        };

        await writer.WriteLineAsync(string.Join(request.Separator.ToString(), columns)).ConfigureAwait(false);

        var total = Math.Max(1, rows.Count);
        var written = 0;
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fields = new List<string>(columns.Count);
            foreach (var column in columns)
            {
                fields.Add(FormatField(RenderColumn(column, row, nodes), request));
            }

            await writer.WriteLineAsync(string.Join(request.Separator.ToString(), fields)).ConfigureAwait(false);
            written++;
            if (written % 100 == 0)
            {
                progress?.Report(written * 100 / total);
            }
        }

        await writer.FlushAsync().ConfigureAwait(false);
        progress?.Report(100);
        _ = fileName;
    }

    private sealed record NodeInfo(string Id, string? ParentId, string Kind, string Name, Dictionary<string, object?> Properties);

    private static Dictionary<string, NodeInfo> IndexRows(List<Dictionary<string, object?>> rows)
    {
        var nodes = new Dictionary<string, NodeInfo>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var id = Convert.ToString(row.TryGetValue("id", out var idValue) ? idValue : null, CultureInfo.InvariantCulture) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id) || nodes.ContainsKey(id))
            {
                continue;
            }

            var parentId = row.TryGetValue("parentId", out var parentValue)
                ? Convert.ToString(parentValue, CultureInfo.InvariantCulture)
                : null;
            var kind = Convert.ToString(row.TryGetValue("type", out var typeValue) ? typeValue : null, CultureInfo.InvariantCulture) ?? string.Empty;
            var name = Convert.ToString(row.TryGetValue("name", out var nameValue) ? nameValue : null, CultureInfo.InvariantCulture) ?? string.Empty;
            var properties = row.TryGetValue("properties", out var propsValue)
                && propsValue is Dictionary<string, object?> props
                    ? props
                    : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            nodes[id] = new NodeInfo(id, string.IsNullOrWhiteSpace(parentId) ? null : parentId, kind, name, properties);
        }

        return nodes;
    }

    private static string RenderColumn(string column, Dictionary<string, object?> row, Dictionary<string, NodeInfo> nodes)
    {
        var id = Convert.ToString(row.TryGetValue("id", out var idValue) ? idValue : null, CultureInfo.InvariantCulture) ?? string.Empty;
        nodes.TryGetValue(id, out var node);

        var volume = node is null ? string.Empty : VolumeOf(node, nodes);
        var relativePath = node is null ? string.Empty : RelativePathOf(node, nodes);

        if (column.Equals("VOLUME", StringComparison.OrdinalIgnoreCase)) return volume;
        if (column.Equals("CATEGORY", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        if (column.Equals("PATH", StringComparison.OrdinalIgnoreCase)) return relativePath;
        if (column.Equals("FULLPATH", StringComparison.OrdinalIgnoreCase))
        {
            if (node is null || string.IsNullOrWhiteSpace(node.Name))
            {
                return string.Empty;
            }

            var prefix = string.IsNullOrWhiteSpace(volume) ? string.Empty : volume + "/";
            var middle = string.IsNullOrWhiteSpace(relativePath) ? string.Empty : relativePath + "/";
            return prefix + middle + node.Name;
        }

        if (column.Equals("ITEMTYPE", StringComparison.OrdinalIgnoreCase)
            || column.Equals("KIND", StringComparison.OrdinalIgnoreCase))
        {
            return node?.Kind switch
            {
                "media" => "Volume",
                "folder" => "Folder",
                _ => "File"
            };
        }

        if (node is null)
        {
            return string.Empty;
        }

        if (column.Equals("NAME", StringComparison.OrdinalIgnoreCase)) return node.Name;
        if (column.Equals("SIZE", StringComparison.OrdinalIgnoreCase))
        {
            return Convert.ToString(row.TryGetValue("size", out var sizeValue) ? sizeValue : null, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        if (column.Equals("DATE", StringComparison.OrdinalIgnoreCase)) return PropertyOf(node, "Date");
        if (column.Equals("TIME", StringComparison.OrdinalIgnoreCase)) return PropertyOf(node, "Time");
        if (column.Equals("TYPE", StringComparison.OrdinalIgnoreCase)) return PropertyOf(node, "FormatType");
        if (column.Equals("DESC", StringComparison.OrdinalIgnoreCase)) return PropertyOf(node, "Description");
        if (column.Equals("FILES", StringComparison.OrdinalIgnoreCase)) return PropertyOf(node, "FilesCount");
        if (column.Equals("FOLDERS", StringComparison.OrdinalIgnoreCase)) return PropertyOf(node, "FoldersCount");

        return string.Empty;
    }

    private static string VolumeOf(NodeInfo node, Dictionary<string, NodeInfo> nodes)
    {
        var current = node;
        var guard = 0;
        while (current.ParentId is not null && nodes.TryGetValue(current.ParentId, out var parent) && guard++ < 10000)
        {
            current = parent;
        }

        return current.Name;
    }

    private static string RelativePathOf(NodeInfo node, Dictionary<string, NodeInfo> nodes)
    {
        var segments = new Stack<string>();
        var current = node;
        var guard = 0;
        while (current.ParentId is not null && nodes.TryGetValue(current.ParentId, out var parent) && guard++ < 10000)
        {
            if (parent.ParentId is not null)
            {
                segments.Push(parent.Name);
            }

            current = parent;
        }

        return string.Join("/", segments);
    }

    private static string PropertyOf(NodeInfo node, string key)
    {
        if (node.Properties.TryGetValue(key, out var value) && value is not null)
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return string.Empty;
    }

    private static string FormatField(string value, DiskExplorerDelimitedExportRequest request)
    {
        var guarded = value;
        var wasGuarded = false;
        if (request.GuardFormulas && !request.RawCompatibility
            && value.Length > 0 && ("=+-@".Contains(value[0], StringComparison.Ordinal)))
        {
            guarded = "'" + value;
            wasGuarded = true;
        }

        if (wasGuarded
            || guarded.Contains(request.Separator, StringComparison.Ordinal)
            || guarded.Contains('"')
            || guarded.Contains('\r')
            || guarded.Contains('\n'))
        {
            return "\"" + guarded.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        return guarded;
    }

    private static DiskExplorerDelimitedExportRequest NormalizeRequest(object? payload)
    {
        if (payload is DiskExplorerDelimitedExportRequest request)
        {
            ArgumentNullException.ThrowIfNull(request.Rows);
            return request;
        }

        if (payload is IEnumerable<Dictionary<string, object?>> rows)
        {
            return new DiskExplorerDelimitedExportRequest { Rows = rows };
        }

        throw new DiskExplorerDelimitedMalformedException("Export payload must be catalog rows or delimited export options.");
    }
}
