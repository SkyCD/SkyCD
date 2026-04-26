using System.Text;
using System.Text.Json;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

namespace SkyCD.Plugin.Csv;

public sealed class CsvCatalogPlugin : IFileFormatPluginCapability
{
    private static readonly string[] HeaderColumns = ["NodeId", "ParentId", "Kind", "Name", "SizeBytes"];

    public FileFormatDescriptor SupportedFormat =>
        new FileFormatDescriptor(
            "skycd-csv",
            "SkyCD CSV",
            [".csv"],
            CanRead: true,
            CanWrite: true,
            MimeType: "text/csv");

    public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            using var reader = new StreamReader(request.Source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var headerLine = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(headerLine))
            {
                return new FileFormatReadResult { Success = true, Payload = new List<Dictionary<string, object?>>() };
            }

            var actualHeader = ParseCsvLine(headerLine);
            if (!HeaderColumns.SequenceEqual(actualHeader, StringComparer.OrdinalIgnoreCase))
            {
                return new FileFormatReadResult
                {
                    Success = false,
                    Error = "CSV header must match: NodeId,ParentId,Kind,Name,SizeBytes."
                };
            }

            var rows = new List<Dictionary<string, object?>>();
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
            {
                if (line.Length == 0)
                {
                    continue;
                }

                var fields = ParseCsvLine(line);
                if (fields.Count != HeaderColumns.Length)
                {
                    return new FileFormatReadResult
                    {
                        Success = false,
                        Error = $"CSV row has {fields.Count} fields, expected {HeaderColumns.Length}."
                    };
                }

                rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["NodeId"] = fields[0],
                    ["ParentId"] = fields[1],
                    ["Kind"] = fields[2],
                    ["Name"] = fields[3],
                    ["SizeBytes"] = fields[4]
                });
            }

            return new FileFormatReadResult
            {
                Success = true,
                Payload = rows
            };
        }
        catch (Exception exception)
        {
            return new FileFormatReadResult
            {
                Success = false,
                Error = exception.Message
            };
        }
    }

    public async Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = ResolveRows(request.Payload);
            using var writer = new StreamWriter(request.Target, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
            await writer.WriteLineAsync(string.Join(",", HeaderColumns));

            foreach (var row in rows)
            {
                var values = HeaderColumns
                    .Select(column => EscapeCsv(row.TryGetValue(column, out var value) ? value?.ToString() : null));
                await writer.WriteLineAsync(string.Join(",", values));
            }

            await writer.FlushAsync(cancellationToken);
            return new FileFormatWriteResult { Success = true };
        }
        catch (Exception exception)
        {
            return new FileFormatWriteResult
            {
                Success = false,
                Error = exception.Message
            };
        }
    }

    private static List<Dictionary<string, object?>> ResolveRows(object? payload)
    {
        if (payload is List<Dictionary<string, object?>> rows)
        {
            return rows;
        }

        if (payload is JsonElement { ValueKind: JsonValueKind.Array } arrayElement)
        {
            return arrayElement.EnumerateArray()
                .Select(element => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["NodeId"] = ReadJsonValue(element, "nodeId"),
                    ["ParentId"] = ReadJsonValue(element, "parentId"),
                    ["Kind"] = ReadJsonValue(element, "kind"),
                    ["Name"] = ReadJsonValue(element, "name"),
                    ["SizeBytes"] = ReadJsonValue(element, "sizeBytes")
                })
                .ToList();
        }

        throw new InvalidOperationException("CSV payload must be a row list or JSON array.");
    }

    private static string? ReadJsonValue(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            ? property.ToString()
            : null;
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var escaped = value.Replace("\"", "\"\"");
        return escaped.Contains(',') || escaped.Contains('"') || escaped.Contains('\n') || escaped.Contains('\r')
            ? $"\"{escaped}\""
            : escaped;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var buffer = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var current = line[index];
            if (current == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    buffer.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (current == ',' && !inQuotes)
            {
                result.Add(buffer.ToString());
                buffer.Clear();
            }
            else
            {
                buffer.Append(current);
            }
        }

        result.Add(buffer.ToString());
        return result;
    }
}
