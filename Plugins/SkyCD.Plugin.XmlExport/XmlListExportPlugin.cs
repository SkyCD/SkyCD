using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.XmlExport.Exceptions;

namespace SkyCD.Plugin.XmlExport;

/// <summary>
/// Export-only flat-list XML writer (format id <c>skycd-xml-list</c>).
/// Writes the current list/result set as an <c>ItemList</c> document whose
/// <c>Item</c> children follow the user's selected column order. Display labels
/// normalize to valid XML element names; the original label travels in a
/// <c>Label</c> attribute whenever normalization changes it. Items stream through
/// an <see cref="XmlWriter"/> so large result sets are never buffered as XML.
/// </summary>
public sealed class XmlListExportPlugin : IFileFormatPluginCapability
{
    private const int ProgressReportEvery = 64;

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "skycd-xml-list",
            DisplayName: "SkyCD XML List Export",
            Extensions: [".xml"],
            MimeTypes: ["application/xml", "text/xml"],
            CanRead: false,
            CanWrite: true);

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new FileFormatReadResult
        { Success = false, Error = "SkyCD XML list export is write-only." });
    }

    public async Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (rows, databaseName, stylesheet, columns) =
                NormalizeRequest(request.Payload, request.FileName);

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

            await writer.WriteStartElementAsync(null, "ItemList", null);
            await writer.WriteAttributeStringAsync(null, "Database", null, databaseName);

            var total = rows is ICollection<Dictionary<string, object?>> sized ? sized.Count : (int?)null;
            var written = 0;
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WriteItemAsync(writer, row, columns, written);
                written++;
                if (written % ProgressReportEvery == 0 || (total.HasValue && written == total.Value))
                {
                    request.Progress?.Report(total.HasValue && total.Value > 0
                        ? Math.Clamp(written * 100 / total.Value, 0, 100)
                        : Math.Min(99, written / ProgressReportEvery));
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

    private static (IEnumerable<Dictionary<string, object?>> Rows, string DatabaseName, string? Stylesheet,
        IReadOnlyList<string>? Columns) NormalizeRequest(object? payload, string? fileName)
    {
        if (payload is XmlExportRequest request)
        {
            ArgumentNullException.ThrowIfNull(request.Rows);
            return (request.Rows,
                XmlExport.ResolveDatabaseName(request.DatabaseName, fileName, "(database)"),
                XmlExport.ValidateStylesheet("(database)", request.Stylesheet),
                request.Columns is { Count: > 0 } columns ? columns : null);
        }

        if (payload is IEnumerable<Dictionary<string, object?>> rows)
        {
            return (rows, XmlExport.ResolveDatabaseName(null, fileName, "(database)"), null, null);
        }

        throw new XmlExportException("Payload must be result rows or an XML export request.");
    }

    private static async Task WriteItemAsync(XmlWriter writer, Dictionary<string, object?> row,
        IReadOnlyList<string>? columns, int index)
    {
        var identity = DescribeRow(row, index);
        var type = XmlExport.MapKindType(identity, XmlExport.GetValue(row, "kind"));

        await writer.WriteStartElementAsync(null, "Item", null);
        await writer.WriteAttributeStringAsync(null, "Type", null, type);

        if (columns is not null)
        {
            foreach (var column in columns)
            {
                await WriteColumnAsync(writer, row, identity, column);
            }
        }
        else
        {
            foreach (var (key, _) in row)
            {
                if (XmlExport.IsStructuralListKey(key))
                {
                    continue;
                }

                await WriteColumnAsync(writer, row, identity, key);
            }
        }

        await writer.WriteEndElementAsync();
    }

    private static async Task WriteColumnAsync(XmlWriter writer, Dictionary<string, object?> row,
        string identity, string column)
    {
        var element = ResolveElementName(column, out var canonical);
        var raw = ResolveColumnValue(row, column, canonical);
        string? text;

        if (canonical is not null && XmlExport.IsSizeKey(canonical))
        {
            text = XmlExport.FormatSize(identity, column, raw).ToString(CultureInfo.InvariantCulture);
        }
        else if (canonical is not null && XmlExport.IsDateKey(canonical))
        {
            text = XmlExport.FormatTimestamp(identity, column, raw);
        }
        else
        {
            text = raw;
        }

        if (text is null)
        {
            text = string.Empty;
        }

        XmlExport.ValidateText(identity, column, text);
        await writer.WriteStartElementAsync(null, element, null);
        if (!element.Equals(column, StringComparison.Ordinal))
        {
            XmlExport.ValidateText(identity, column, column);
            await writer.WriteAttributeStringAsync(null, "Label", null, column);
        }

        if (text.Length > 0)
        {
            await writer.WriteStringAsync(text);
        }

        await writer.WriteEndElementAsync();
    }

    private static string ResolveElementName(string column, out string? canonical)
    {
        if (column.Equals("name", StringComparison.OrdinalIgnoreCase))
        {
            canonical = "name";
            return "Name";
        }

        if (XmlExport.IsSizeKey(column))
        {
            canonical = "size";
            return "Size";
        }

        if (XmlExport.IsDateKey(column))
        {
            canonical = "modified";
            return "Modified";
        }

        canonical = null;
        return XmlExport.NormalizeElementName(column, out _);
    }

    private static string? ResolveColumnValue(Dictionary<string, object?> row, string column, string? canonical)
    {
        if (row.TryGetValue(column, out var direct))
        {
            return XmlExport.ToDisplayText(direct);
        }

        if (canonical is not null)
        {
            string[] aliases = canonical switch
            {
                "name" => ["name"],
                "size" => ["size", "sizebytes"],
                "modified" => ["date", "modified", "modifiedutc", "lastmodified", "lastmodifiedutc"],
                _ => [column]
            };
            foreach (var alias in aliases)
            {
                var aliased = XmlExport.GetValue(row, alias);
                if (aliased is not null)
                {
                    return aliased;
                }
            }

            return null;
        }

        return XmlExport.GetValue(row, column);
    }

    private static string DescribeRow(Dictionary<string, object?> row, int index)
    {
        var name = XmlExport.GetValue(row, "name");
        return !string.IsNullOrWhiteSpace(name) ? name.Trim() : $"row {index + 1}";
    }
}
