using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

namespace SkyCD.Plugin.Legacy.Ascd;

public sealed class LegacyAscdPlugin : IFileFormatPluginCapability
{
    private const string FormatHeaderPrefix = "# format: skycd-nf";
    private static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "legacy-ascd",
            DisplayName: "SkyCD Advanced Format",
            Extensions: [".ascd"],
            MimeTypes: ["application/vnd.skycd.ascd"],
            CanRead: true,
            CanWrite: true);

    public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var compressed = new DeflateStream(request.Source, CompressionMode.Decompress, leaveOpen: true);
            using var reader = new StreamReader(compressed, Encoding.UTF8, leaveOpen: true);

            var lineNumber = 0;
            string? header = null;
            while ((header = await reader.ReadLineAsync(cancellationToken)) is not null)
            {
                lineNumber++;
                if (!string.IsNullOrWhiteSpace(header))
                {
                    break;
                }
            }

            if (header is null || !TryParseHeaderVersion(header, out var version))
            {
                return new FileFormatReadResult
                {
                    Success = false,
                    Error = "Missing or invalid header. Expected '# format: skycd-nf <version>'."
                };
            }

            var documents = new List<Dictionary<string, object?>>();
            string? line;
            var processed = 0;
            while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var trimmedLine = line.Trim();
                if (TryParseDocumentRecord(trimmedLine, out var document))
                {
                    documents.Add(document);
                    processed++;
                    request.Progress?.Report(Math.Min(99, processed % 100));
                    continue;
                }

                if (!LooksLikeLegacyRecord(trimmedLine))
                {
                    continue;
                }

                if (!TryParseLegacyRecord(trimmedLine, out var entry, out var legacyError))
                {
                    return new FileFormatReadResult
                    {
                        Success = false,
                        Error = $"Line {lineNumber}: {legacyError}"
                    };
                }

                documents.Add(ToDocument(entry));
                processed++;
                request.Progress?.Report(Math.Min(99, processed % 100));
            }

            request.Progress?.Report(100);
            return new FileFormatReadResult
            {
                Success = true,
                Payload = documents
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

    public async Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entries = ResolveEntries(request.Payload, out var versionHint);
            using var compressed = new DeflateStream(request.Target, CompressionMode.Compress, leaveOpen: true);
            using var writer = new StreamWriter(compressed, Encoding.UTF8, leaveOpen: true);

            var version = string.IsNullOrWhiteSpace(versionHint) ? "1.0" : versionHint.Trim();
            await writer.WriteLineAsync($"{FormatHeaderPrefix} {version}");

            for (var index = 0; index < entries.Count; index++)
            {
                var line = BuildDocumentRecordLine(entries[index]);
                await writer.WriteLineAsync(line.AsMemory(), cancellationToken);

                request.Progress?.Report((int)((index + 1d) / Math.Max(1, entries.Count) * 100d));
            }

            await writer.FlushAsync(cancellationToken);
            request.Progress?.Report(100);
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

    private static bool TryParseHeaderVersion(string headerLine, out string version)
    {
        var trimmed = headerLine.Trim();
        if (!trimmed.StartsWith(FormatHeaderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            version = string.Empty;
            return false;
        }

        version = trimmed.Length == FormatHeaderPrefix.Length
            ? "1.0"
            : trimmed[FormatHeaderPrefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(version))
        {
            version = "1.0";
        }

        return true;
    }

    private static bool TryParseLegacyRecord(string line, out LegacyAscdEntry entry, out string error)
    {
        if (!LooksLikeLegacyRecord(line))
        {
            entry = null!;
            error = "Unsupported record line format.";
            return false;
        }

        // Trim trailing semicolons and whitespaces for legacy parser tolerance
        line = line.TrimEnd(' ', '\t', '\r', '\n', ';');
        var valuesTokenIndex = line.IndexOf("VALUES", StringComparison.OrdinalIgnoreCase);
        if (valuesTokenIndex < 0)
        {
            entry = null!;
            error = "Missing VALUES clause.";
            return false;
        }

        var openParen = line.IndexOf('(', valuesTokenIndex);
        // the drop table might have parenthesis? Usually not. Let's find the closing parenthesis of the VALUES clause.

        var closeParen = -1;
        var inQuotes = false;
        for (var i = openParen + 1; i < line.Length; i++)
        {
            if (line[i] == '\'' && (i == 0 || line[i - 1] != '\\'))
            {
                // Quote tracking for legacy escaped quotes ('')
                inQuotes = !inQuotes;
            }

            if (!inQuotes && line[i] == ')')
            {
                closeParen = i;
                break;
            }
        }

        if (closeParen <= openParen)
        {
            entry = null!;
            error = "Only a single VALUES(...) statement is supported.";
            return false;
        }

        if (!TryParseQuotedFields(line, openParen + 1, closeParen - 1, out var values, out error))
        {
            entry = null!;
            return false;
        }

        if (values.Count != 7)
        {
            entry = null!;
            error = $"Expected 7 values but found {values.Count}.";
            return false;
        }

        var appId = values[6];
        if (appId == "<?Application_ID?>")
        {
            appId = Guid.Empty.ToString();
        }

        entry = new LegacyAscdEntry
        {
            Id = values[0],
            Name = values[1],
            ParentId = values[2],
            Type = values[3],
            PropertiesXml = values[4],
            SizeBytes = TryParseSize(values[5]),
            ApplicationId = appId
        };
        error = string.Empty;
        return true;
    }

    private static bool TryParseQuotedFields(string text, int startIndex, int endIndex, out List<string> values,
        out string error)
    {
        values = [];
        var index = startIndex;

        while (index <= endIndex)
        {
            while (index <= endIndex && char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            if (index > endIndex || text[index] != '\'')
            {
                error = "Expected quoted value.";
                return false;
            }

            index++;
            var builder = new StringBuilder();
            var closed = false;
            while (index <= endIndex)
            {
                var ch = text[index];
                if (ch == '\'')
                {
                    if (index + 1 <= endIndex && text[index + 1] == '\'')
                    {
                        builder.Append('\'');
                        index += 2;
                        continue;
                    }

                    closed = true;
                    index++;
                    break;
                }

                builder.Append(ch);
                index++;
            }

            if (!closed)
            {
                error = "Unterminated quoted value.";
                return false;
            }

            values.Add(builder.ToString());

            while (index <= endIndex && char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            if (index <= endIndex)
            {
                if (text[index] != ',')
                {
                    error = "Expected comma delimiter between values.";
                    return false;
                }

                index++;
            }
        }

        error = string.Empty;
        return true;
    }

    private static long TryParseSize(string raw) =>
        long.TryParse(raw, out var parsed) ? parsed : 0L;

    private static Dictionary<string, object?> ToDocument(LegacyAscdEntry entry)
    {
        var normalizedType = NormalizeLegacyType(entry.Type);
        var parentId = entry.ParentId is "-1" or "" ? null : entry.ParentId;

        return new Dictionary<string, object?>(KeyComparer)
        {
            ["id"] = entry.Id,
            ["name"] = entry.Name,
            ["parentId"] = parentId,
            ["type"] = normalizedType,
            ["size"] = entry.SizeBytes,
            ["childrenCount"] = 0L,
            ["properties"] = entry.PropertiesXml,
            // Compatibility
            ["legacyType"] = entry.Type,
            ["applicationId"] = string.IsNullOrWhiteSpace(entry.ApplicationId) ? Guid.Empty.ToString() : entry.ApplicationId
        };
    }

    private static bool TryParseDocumentRecord(string line, out Dictionary<string, object?> document)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            document = null!;
            return false;
        }

        try
        {
            using var parsed = JsonDocument.Parse(line);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                document = default!;
                return false;
            }

            document = new Dictionary<string, object?>(KeyComparer);
            foreach (var property in parsed.RootElement.EnumerateObject())
            {
                document[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.Number when property.Value.TryGetInt64(out var number) => number,
                    _ => property.Value.ToString()
                };
            }

            return true;
        }
        catch (JsonException)
        {
            document = null!;
            return false;
        }
    }

    private static string BuildDocumentRecordLine(LegacyAscdEntry entry)
    {
        var document = ToDocument(entry);
        return JsonSerializer.Serialize(document);
    }

    private static bool LooksLikeLegacyRecord(string line)
    {
        var valuesTokenIndex = line.IndexOf("VALUES", StringComparison.OrdinalIgnoreCase);
        if (valuesTokenIndex < 0)
        {
            return false;
        }

        var openParen = line.IndexOf('(', valuesTokenIndex);
        return openParen >= 0;
    }

    private static List<LegacyAscdEntry> ResolveEntries(object? payload, out string? headerVersion)
    {
        headerVersion = "1.0";

        if (payload is LegacyAscdCatalog catalog)
        {
            headerVersion = catalog.HeaderVersion;
            return catalog.Entries;
        }

        if (payload is IEnumerable<Dictionary<string, object?>> rows)
        {
            return rows.Select(FromDocument).ToList();
        }

        if (payload is JsonElement { ValueKind: JsonValueKind.Array } arrayElement)
        {
            return arrayElement.EnumerateArray()
                .Where(static element => element.ValueKind == JsonValueKind.Object)
                .Select(FromJsonDocument)
                .ToList();
        }

        throw new InvalidOperationException("Payload must be document rows or LegacyAscdCatalog.");
    }

    private static LegacyAscdEntry FromDocument(Dictionary<string, object?> row)
    {
        var normalizedType = GetString(row, "type", defaultValue: "File");

        return new LegacyAscdEntry
        {
            Id = GetString(row, "id", defaultValue: Guid.NewGuid().ToString()),
            Name = GetString(row, "name"),
            ParentId = GetString(row, "parentId", defaultValue: "-1"),
            Type = GetString(row, "legacyType", defaultValue: DenormalizeLegacyType(normalizedType)),
            PropertiesXml = GetString(row, "properties", defaultValue: GetString(row, "propertiesXml")),
            SizeBytes = GetLong(row, "size", "sizeBytes"),
            ApplicationId = GetString(row, "applicationId", defaultValue: Guid.Empty.ToString())
        };
    }

    private static LegacyAscdEntry FromJsonDocument(JsonElement element)
    {
        var normalizedType = ReadJsonString(element, "type") ?? "File";

        return new LegacyAscdEntry
        {
            Id = ReadJsonString(element, "id") ?? Guid.NewGuid().ToString(),
            Name = ReadJsonString(element, "name") ?? string.Empty,
            ParentId = ReadJsonString(element, "parentId") ?? "-1",
            Type = ReadJsonString(element, "legacyType") ?? DenormalizeLegacyType(normalizedType),
            PropertiesXml = ReadJsonString(element, "properties") ?? ReadJsonString(element, "propertiesXml") ?? string.Empty,
            SizeBytes = ReadJsonLong(element, "size") ?? ReadJsonLong(element, "sizeBytes") ?? 0L,
            ApplicationId = ReadJsonString(element, "applicationId") ?? Guid.Empty.ToString()
        };
    }

    private static string GetString(Dictionary<string, object?> row, string key, string defaultValue = "")
    {
        if (row.TryGetValue(key, out var value) && value is not null)
        {
            var raw = value.ToString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                return raw;
            }
        }

        return defaultValue;
    }

    private static long GetLong(Dictionary<string, object?> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!row.TryGetValue(key, out var value) || value is null)
            {
                continue;
            }

            if (value is long directLong)
            {
                return directLong;
            }

            if (long.TryParse(value.ToString(), out var parsed))
            {
                return parsed;
            }
        }

        return 0L;
    }

    private static string? ReadJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) ? property.ToString() : null;
    }

    private static long? ReadJsonLong(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt64(out var number) => number,
            _ when long.TryParse(property.ToString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static string NormalizeLegacyType(string legacyType)
    {
        return legacyType.Trim().ToLowerInvariant() switch
        {
            "scdfolder" => "Folder",
            "scdmedia" => "Media",
            "scdnetworkresource" => "NetworkResource",
            _ => "File"
        };
    }

    private static string DenormalizeLegacyType(string normalizedType)
    {
        return normalizedType.Trim().ToLowerInvariant() switch
        {
            "folder" => "scdFolder",
            "media" => "scdMedia",
            "networkresource" => "scdNetworkResource",
            _ => "scdFile"
        };
    }
}
