using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

namespace SkyCD.Plugin.Legacy.Ascd;

public sealed class LegacyAscdPlugin : IFileFormatPluginCapability
{
    private const string FormatHeaderPrefix = "# format: skycd-nf";
    private const string InsertPrefix = "INSERT INTO list";

    public FileFormatDescriptor SupportedFormat =>
        new FileFormatDescriptor("legacy-ascd", "SkyCD Advanced Format", [".ascd"], CanRead: true, CanWrite: true, "application/octet-stream")
    ;

    public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default)
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

            var catalog = new LegacyAscdCatalog { HeaderVersion = version };
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
                if (!trimmedLine.StartsWith(InsertPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryParseInsertStatement(trimmedLine, out var entry, out var error))
                {
                    return new FileFormatReadResult
                    {
                        Success = false,
                        Error = $"Line {lineNumber}: {error}"
                    };
                }

                catalog.Entries.Add(entry);
                processed++;
                request.Progress?.Report(Math.Min(99, processed % 100));
            }

            request.Progress?.Report(100);
            return new FileFormatReadResult
            {
                Success = true,
                Payload = catalog
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
        if (request.Payload is not LegacyAscdCatalog catalog)
        {
            return new FileFormatWriteResult
            {
                Success = false,
                Error = "Payload must be LegacyAscdCatalog."
            };
        }

        try
        {
            using var compressed = new DeflateStream(request.Target, CompressionMode.Compress, leaveOpen: true);
            using var writer = new StreamWriter(compressed, Encoding.UTF8, leaveOpen: true);

            var version = string.IsNullOrWhiteSpace(catalog.HeaderVersion) ? "1.0" : catalog.HeaderVersion.Trim();
            await writer.WriteLineAsync($"{FormatHeaderPrefix} {version}");

            for (var index = 0; index < catalog.Entries.Count; index++)
            {
                var entry = catalog.Entries[index];
                var line = BuildInsertStatement(entry);
                await writer.WriteLineAsync(line.AsMemory(), cancellationToken);

                request.Progress?.Report((int)((index + 1d) / Math.Max(1, catalog.Entries.Count) * 100d));
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

    private static bool TryParseInsertStatement(string line, out LegacyAscdEntry entry, out string error)
    {
        if (!line.StartsWith(InsertPrefix, StringComparison.OrdinalIgnoreCase))
        {
            entry = default!;
            error = "Unsupported statement. Only INSERT INTO list is accepted.";
            return false;
        }

        // Trim trailing semicolons and whitespaces for legacy parser tolerance
        line = line.TrimEnd(' ', '\t', '\r', '\n', ';');
        var valuesTokenIndex = line.IndexOf("VALUES", StringComparison.OrdinalIgnoreCase);
        if (valuesTokenIndex < 0)
        {
            entry = default!;
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
                // Simple quote tracking (it's actually '' for escaped quotes in SQL, but this is simple enough)
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
            entry = default!;
            error = "Only a single VALUES(...) statement is supported.";
            return false;
        }

        if (!TryParseQuotedValues(line, openParen + 1, closeParen - 1, out var values, out error))
        {
            entry = default!;
            return false;
        }

        if (values.Count != 7)
        {
            entry = default!;
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

    private static bool TryParseQuotedValues(string text, int startIndex, int endIndex, out List<string> values, out string error)
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
                error = "Expected quoted SQL literal.";
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
                error = "Unterminated quoted SQL literal.";
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
                    error = "Expected comma delimiter between SQL values.";
                    return false;
                }

                index++;
            }
        }

        error = string.Empty;
        return true;
    }

    private static string BuildInsertStatement(LegacyAscdEntry entry)
    {
        return $"INSERT INTO list (`ID`, `Name`, `ParentID`, `Type`, `Properties`,`Size`, `AID`) VALUES ('{EscapeSqlLiteral(entry.Id)}', '{EscapeSqlLiteral(entry.Name)}', '{EscapeSqlLiteral(entry.ParentId)}', '{EscapeSqlLiteral(entry.Type)}', '{EscapeSqlLiteral(entry.PropertiesXml)}', '{entry.SizeBytes}', '{EscapeSqlLiteral(entry.ApplicationId)}')";
    }

    private static string EscapeSqlLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static long TryParseSize(string raw) =>
        long.TryParse(raw, out var parsed) ? parsed : 0L;
}
