using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Csv.Exceptions;
using SkyCD.Plugin.Csv.Models;

namespace SkyCD.Plugin.Csv;

/// <summary>
/// Export-only catalog-list CSV writer (format id <c>skycd-csv-list</c>).
/// Writes the current list/result set with selectable ordered columns, a configurable
/// one-character separator, explicit encodings, an optional header row, RFC 4180 quoting
/// and CRLF line endings. Sizes render culture-invariantly, timestamps as ISO-8601,
/// nulls as empty fields, and spreadsheet-formula-looking values are neutralized unless
/// the request explicitly opts into raw mode. Rows stream through the writer so large
/// exports never load the full result set into memory.
/// </summary>
public sealed class CsvListExportPlugin : IFileFormatPluginCapability
{
    public static readonly IReadOnlyList<string> DefaultColumns =
        ["Name", "Path", "Kind", "SizeBytes", "Modified", "Description", "Disk"];

    private static readonly Dictionary<string, string[]> ColumnAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = ["name"],
            ["Path"] = ["path", "fullpath", "relativepath", "full_path"],
            ["Kind"] = ["kind", "type", "entrykind"],
            ["SizeBytes"] = ["sizebytes", "size"],
            ["Modified"] = ["modified", "modifiedutc", "lastmodified", "lastmodifiedutc", "date"],
            ["Description"] = ["description", "comment", "info"],
            ["Disk"] = ["disk", "media", "medianame", "diskname"]
        };

    private static readonly char[] FormulaTriggers = ['=', '+', '-', '@'];

    // Sentinel returned by FormatValue to indicate "write this as a quoted empty field".
    private const string QuotedEmptySentinel = "\x00EMPTY\x00";

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "skycd-csv-list",
            DisplayName: "Catalog List CSV export (*.csv)",
            Extensions: [".csv"],
            MimeTypes: ["text/csv"],
            CanRead: false,
            CanWrite: true);

    public async Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var export = NormalizeRequest(request.Payload);
            var encoding = ResolveEncoding(export.EncodingName);
            ValidateSeparator(export.Separator);

            var columns = export.Columns is { Count: > 0 }
                ? export.Columns.ToArray()
                : DefaultColumns.ToArray();

            // CsvHelper handles RFC 4180 quoting (separator, quotes, CR, LF in fields).
            // We configure WhenNeededOrWhitespace so leading/trailing spaces also trigger quoting.
            // Formula neutralization (prefix with ') is applied before passing to CsvHelper.
            var options = new CsvWriterOptions
            {
                Separator = export.Separator,
                NewLine = "\r\n",
                Mode = CsvMode.Rfc4180,
                Quoting = CsvQuoting.WhenNeededOrWhitespace,
                Injection = CsvInjection.None,
                WriteByteOrderMark = false,
                HasHeader = false
            };

            // StreamWriter carries the encoding (and writes BOM bytes for UTF-8-with-BOM).
            await using var streamWriter = new StreamWriter(request.Target, encoding, leaveOpen: true);
            await using var csvWriter = CsvWriter.Create(streamWriter, options, leaveOpen: true);

            if (export.IncludeHeader)
            {
                foreach (var column in columns)
                {
                    csvWriter.WriteField(column);
                }

                await csvWriter.EndRecordAsync();
            }

            var total = export.Rows is ICollection<Dictionary<string, object?>> sized ? sized.Count : (int?)null;
            var written = 0;
            foreach (var row in export.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var column in columns)
                {
                    var fieldText = FormatValue(ResolveValue(row, column), column, export);
                    if (ReferenceEquals(fieldText, QuotedEmptySentinel))
                    {
                        // Empty string — write with forced quoting to distinguish from null.
                        csvWriter.WriteField(string.Empty, CsvQuoting.Always);
                    }
                    else if (fieldText is null || fieldText.Length == 0)
                    {
                        // Null field — write as empty without quoting.
                        csvWriter.WriteField(string.Empty, CsvQuoting.WhenNeeded);
                    }
                    else
                    {
                        // CsvHelper quotes the value when it contains the separator,
                        // a quote, CR, LF, or leading/trailing whitespace.
                        csvWriter.WriteField(fieldText, CsvQuoting.WhenNeededOrWhitespace);
                    }
                }

                await csvWriter.EndRecordAsync();
                written++;
                if (written % 64 == 0 || (total.HasValue && written == total.Value))
                {
                    request.Progress?.Report(total.HasValue && total.Value > 0
                        ? Math.Clamp(written * 100 / total.Value, 0, 100)
                        : Math.Min(99, written / 64));
                }
            }

            request.Progress?.Report(100);
            await csvWriter.FlushAsync();
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

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new FileFormatReadResult
        { Success = false, Error = "Catalog list CSV export is write-only." });
    }

    private static CsvListExportRequest NormalizeRequest(object? payload)
    {
        if (payload is CsvListExportRequest request)
        {
            ArgumentNullException.ThrowIfNull(request.Rows);
            return request;
        }

        return new CsvListExportRequest { Rows = CoerceRows(payload) };
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
                    : throw new CsvListExportException("JSON array items must be objects."));
        }

        throw new CsvListExportException("Payload must be catalog rows or a JSON array of objects.");
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

    private static object? ResolveValue(Dictionary<string, object?> row, string column)
    {
        if (row.TryGetValue(column, out var direct))
        {
            return direct;
        }

        if (ColumnAliases.TryGetValue(column, out var aliases))
        {
            foreach (var alias in aliases)
            {
                if (!alias.Equals(column, StringComparison.OrdinalIgnoreCase)
                    && row.TryGetValue(alias, out var aliased))
                {
                    return aliased;
                }
            }
        }
        else
        {
            foreach (var (key, value) in row)
            {
                if (key.Equals(column, StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Converts a row value to the string to be written. Returns:
    /// <list type="bullet">
    /// <item><see langword="null"/> for a null value (written as empty unquoted)</item>
    /// <item><see cref="QuotedEmptySentinel"/> for an empty-string value (written as quoted <c>""</c>)</item>
    /// <item>A non-empty string — possibly prefixed with <c>'</c> for formula neutralization —
    /// to be passed to CsvHelper, which applies RFC 4180 quoting as needed.</item>
    /// </list>
    /// </summary>
    private static string? FormatValue(object? value, string column, CsvListExportRequest export)
    {
        if (value is null)
        {
            return null;
        }

        var text = column.Equals("SizeBytes", StringComparison.OrdinalIgnoreCase)
            ? FormatSize(value)
            : column.Equals("Modified", StringComparison.OrdinalIgnoreCase)
                ? FormatTimestamp(value)
                : ToDisplayText(value);
        if (text is null)
        {
            return null;
        }

        if (text.Length == 0)
        {
            // Quoted empty distinguishes a literal empty string from a null field.
            return QuotedEmptySentinel;
        }

        if (!export.AllowFormulas && StartsLikeFormula(text))
        {
            text = "'" + text;
        }

        return text;
    }

    private static bool StartsLikeFormula(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var current = text[i];
            if (current is ' ' or '\t')
            {
                continue;
            }

            return FormulaTriggers.Contains(current);
        }

        return false;
    }

    private static string? FormatSize(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is long integer)
        {
            return integer.ToString(CultureInfo.InvariantCulture);
        }

        if (value is int small)
        {
            return small.ToString(CultureInfo.InvariantCulture);
        }

        if (value is double number)
        {
            return number.ToString("R", CultureInfo.InvariantCulture);
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLong))
        {
            return parsedLong.ToString(CultureInfo.InvariantCulture);
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDouble))
        {
            return parsedDouble.ToString("R", CultureInfo.InvariantCulture);
        }

        return text;
    }

    private static string? FormatTimestamp(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case DateTimeOffset offset:
                return offset.ToString("o", CultureInfo.InvariantCulture);
            case DateTime dateTime:
                return new DateTimeOffset(
                    dateTime.Kind == DateTimeKind.Unspecified
                        ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
                        : dateTime).ToString("o", CultureInfo.InvariantCulture);
            default:
                return FormatTimestampText(Convert.ToString(value, CultureInfo.InvariantCulture));
        }
    }

    private static string? FormatTimestampText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        string[] formats =
        [
            "o", "yyyy-MM-ddTHH:mm:ss.FFFFFFFK", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd",
            "yyyy/MM/dd HH:mm:ss", "yyyy/MM/dd", "MM/dd/yyyy HH:mm:ss", "MM/dd/yyyy",
            "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy"
        ];
        if (DateTimeOffset.TryParseExact(trimmed, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return parsed.ToString("o", CultureInfo.InvariantCulture);
        }

        return trimmed;
    }

    private static string? ToDisplayText(object? value) => value switch
    {
        null => null,
        string text => text,
        bool flag => flag ? bool.TrueString : bool.FalseString,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)
    };

    private static Encoding ResolveEncoding(string name)
    {
        var normalized = (name ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "utf-8" or "utf8" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            "utf-8-nobom" or "utf8-nobom" or "utf-8-no-bom" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            "windows-1252" or "cp1252" or "1252" => ResolveCodePage(1252),
            "windows-1250" or "cp1250" or "1250" => ResolveCodePage(1250),
            "windows-1251" or "cp1251" or "1251" => ResolveCodePage(1251),
            _ => throw new CsvListExportException($"Unsupported CSV encoding '{name}'.")
        };
    }

    private static Encoding ResolveCodePage(int codePage)
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(codePage);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new CsvListExportException($"Code page {codePage} is not available on this platform.");
        }
    }

    private static void ValidateSeparator(char separator)
    {
        if (separator is '"' or '\r' or '\n' or '\0')
        {
            throw new CsvListExportException("Separator must be a single character other than a quote or line break.");
        }
    }
}
