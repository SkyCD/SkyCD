using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.XmlExport.Exceptions;

namespace SkyCD.Plugin.XmlExport;

/// <summary>
/// Export-only hierarchical XML catalog writer (format id <c>skycd-xml-catalog</c>).
/// Writes the selected catalog items as a nested <c>Broken_X_Database</c> document
/// with <c>Disk</c>, <c>Folder</c>, and <c>File</c> elements carrying <c>Name</c>,
/// unsigned-decimal <c>Size</c>, and ISO-8601 <c>Date</c> attributes. Additional
/// typed metadata travels as child <c>Metadata Name="..." Value="..." /</c>
/// elements. Rows stream through an <see cref="XmlWriter"/> so large catalogs are
/// never held as an XML document in memory.
/// </summary>
public sealed class XmlCatalogExportPlugin : IFileFormatPluginCapability
{
    private const int ProgressReportEvery = 64;

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "skycd-xml-catalog",
            DisplayName: "SkyCD XML Catalog Export",
            Extensions: [".xml"],
            MimeTypes: ["application/xml", "text/xml"],
            CanRead: false,
            CanWrite: true);

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new FileFormatReadResult
        { Success = false, Error = "SkyCD XML catalog export is write-only." });
    }

    public async Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (rows, databaseName, stylesheet) = NormalizeRequest(request.Payload, request.FileName);
            var nodes = BuildNodes(rows);
            var roots = LinkHierarchy(nodes);

            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                Indent = true,
                Async = true,
                OmitXmlDeclaration = true
            };

            await using var writer = XmlWriter.Create(request.Target, settings);
            await writer.WriteRawAsync("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n");
            if (stylesheet is not null)
            {
                await writer.WriteProcessingInstructionAsync("xml-stylesheet",
                    $"type=\"text/xsl\" href=\"{stylesheet}\"");
            }

            await writer.WriteStartElementAsync(null, "Broken_X_Database", null);
            await writer.WriteAttributeStringAsync(null, "Name", null, databaseName);

            var total = nodes.Count;
            var written = 0;
            foreach (var root in roots)
            {
                var stack = new Stack<(CatalogNode Node, bool ChildrenWritten)>();
                stack.Push((root, false));
                while (stack.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var (node, childrenWritten) = stack.Pop();
                    if (!childrenWritten)
                    {
                        await WriteNodeStartAsync(writer, node);
                        if (node.Children.Count == 0)
                        {
                            await writer.WriteEndElementAsync();
                            written = ReportProgress(request, written, total);
                            continue;
                        }

                        stack.Push((node, true));
                        for (var i = node.Children.Count - 1; i >= 0; i--)
                        {
                            stack.Push((node.Children[i], false));
                        }
                    }
                    else
                    {
                        await writer.WriteEndElementAsync();
                        written = ReportProgress(request, written, total);
                    }
                }
            }

            await writer.WriteEndElementAsync();
            await writer.FlushAsync();
            request.Progress?.Report(100);
            return new FileFormatWriteResult { Success = true };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new FileFormatWriteResult { Success = false, Error = exception.Message };
        }
    }

    private static (List<Dictionary<string, object?>> Rows, string DatabaseName, string? Stylesheet)
        NormalizeRequest(object? payload, string? fileName)
    {
        if (payload is XmlExportRequest request)
        {
            ArgumentNullException.ThrowIfNull(request.Rows);
            var rows = new List<Dictionary<string, object?>>(request.Rows);
            return (rows,
                XmlExport.ResolveDatabaseName(request.DatabaseName, fileName, "(database)"),
                XmlExport.ValidateStylesheet("(database)", request.Stylesheet));
        }

        if (payload is IEnumerable<Dictionary<string, object?>> bareRows)
        {
            return (new List<Dictionary<string, object?>>(bareRows),
                XmlExport.ResolveDatabaseName(null, fileName, "(database)"),
                null);
        }

        throw new XmlExportException("Payload must be catalog rows or an XML export request.");
    }

    private static List<CatalogNode> BuildNodes(List<Dictionary<string, object?>> rows)
    {
        var nodes = new List<CatalogNode>(rows.Count);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var identity = DescribeRow(row, index);
            var name = XmlExport.GetValue(row, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new XmlExportInvalidValueException(identity, "Name",
                    "Every exported item must have a name.");
            }

            var trimmedName = name.Trim();
            var sizeRaw = FirstValue(row, "size", "sizebytes");
            var dateValue = FirstRawValue(row, "date", "modified", "modifiedutc", "lastmodified", "lastmodifiedutc");
            var node = new CatalogNode
            {
                Identity = identity,
                NodeId = XmlExport.GetValue(row, "nodeid"),
                ParentId = XmlExport.GetValue(row, "parentid"),
                ElementName = XmlExport.MapKindElement(XmlExport.GetValue(row, "kind")),
                Name = trimmedName,
                Size = XmlExport.FormatSize(identity, "Size", sizeRaw),
                Date = XmlExport.FormatTimestamp(identity, "Date", dateValue)
            };

            XmlExport.ValidateText(identity, "Name", trimmedName);
            foreach (var (key, value) in row)
            {
                if (XmlExport.IsStructuralCatalogKey(key) || value is null)
                {
                    continue;
                }

                var text = XmlExport.ToDisplayText(value) ?? string.Empty;
                XmlExport.ValidateText(identity, key, key);
                XmlExport.ValidateText(identity, key, text);
                node.Metadata.Add((key, text));
            }

            nodes.Add(node);
        }

        return nodes;
    }

    private static List<CatalogNode> LinkHierarchy(List<CatalogNode> nodes)
    {
        var byId = new Dictionary<string, CatalogNode>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (!string.IsNullOrEmpty(node.NodeId) && !byId.ContainsKey(node.NodeId))
            {
                byId[node.NodeId] = node;
            }
        }

        var roots = new List<CatalogNode>(nodes.Count);
        foreach (var node in nodes)
        {
            if (!string.IsNullOrEmpty(node.ParentId)
                && byId.TryGetValue(node.ParentId, out var parent)
                && !ReferenceEquals(parent, node))
            {
                parent.Children.Add(node);
            }
            else
            {
                roots.Add(node);
            }
        }

        return roots;
    }

    private static async Task WriteNodeStartAsync(XmlWriter writer, CatalogNode node)
    {
        await writer.WriteStartElementAsync(null, node.ElementName, null);
        await writer.WriteAttributeStringAsync(null, "Name", null, node.Name);
        await writer.WriteAttributeStringAsync(null, "Size", null,
            node.Size.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (node.Date is not null)
        {
            await writer.WriteAttributeStringAsync(null, "Date", null, node.Date);
        }

        foreach (var (name, value) in node.Metadata)
        {
            await writer.WriteStartElementAsync(null, "Metadata", null);
            await writer.WriteAttributeStringAsync(null, "Name", null, name);
            await writer.WriteAttributeStringAsync(null, "Value", null, value);
            await writer.WriteEndElementAsync();
        }
    }

    private static int ReportProgress(FileFormatWriteRequest request, int written, int total)
    {
        written++;
        if (written % ProgressReportEvery == 0 || written == total)
        {
            request.Progress?.Report(total > 0
                ? Math.Clamp(written * 100 / total, 0, 100)
                : 100);
        }

        return written;
    }

    private static string DescribeRow(Dictionary<string, object?> row, int index)
    {
        var name = XmlExport.GetValue(row, "name");
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name.Trim();
        }

        var nodeId = XmlExport.GetValue(row, "nodeid");
        return !string.IsNullOrWhiteSpace(nodeId) ? $"node {nodeId.Trim()}" : $"row {index + 1}";
    }

    private static string? FirstValue(Dictionary<string, object?> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = XmlExport.GetValue(row, key);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static object? FirstRawValue(Dictionary<string, object?> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            foreach (var (candidate, value) in row)
            {
                if (candidate.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private sealed class CatalogNode
    {
        public required string Identity { get; init; }

        public string? NodeId { get; init; }

        public string? ParentId { get; init; }

        public required string ElementName { get; init; }

        public required string Name { get; init; }

        public ulong Size { get; init; }

        public string? Date { get; init; }

        public List<(string Name, string Value)> Metadata { get; } = [];

        public List<CatalogNode> Children { get; } = [];
    }
}
