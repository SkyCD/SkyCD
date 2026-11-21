using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.PlainText.Exceptions;
using SkyCD.Plugin.PlainText.Models;

namespace SkyCD.Plugin.PlainText;

/// <summary>
/// Export-only human-readable catalog-list writer (format id <c>skycd-txt-list</c>).
/// The default layout is aligned fixed-width text: one header row, one separator
/// row of dashes, one row per item, CRLF endings, and a blank line between groups
/// only when grouping is enabled. An optional tab-delimited layout writes
/// machine-friendly text.
/// </summary>
/// <remarks>
/// Column width is the maximum of header and rendered values capped by the
/// configured limit; longer values truncate with an ellipsis and embedded
/// CR/LF/tab characters normalize to spaces. Size columns right-align, other
/// columns left-align. Widths are measured exactly for re-enumerable sources and
/// over a bounded first pass otherwise; file targets are written to a temporary
/// sibling and atomically replaced after success.
/// </remarks>
public sealed class PlainTextListExportPlugin : IFileFormatPluginCapability
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

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "skycd-txt-list",
            DisplayName: "Catalog List Text Export (*.txt)",
            Extensions: [".txt"],
            MimeTypes: ["text/plain"],
            CanRead: false,
            CanWrite: true);

    public async Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var export = NormalizeRequest(request.Payload);
            var encoding = ResolveEncoding(export.EncodingName);
            var culture = ResolveCulture(export.CultureName);
            var maxWidth = ValidateColumnWidth(export.MaxColumnWidth, nameof(export.MaxColumnWidth));
            var sampleLimit = export.MaxWidthSampleRows <= 0
                ? throw new PlainTextListExportException("MaxWidthSampleRows must be positive.")
                : export.MaxWidthSampleRows;
            var columns = export.Columns is { Count: > 0 }
                ? export.Columns.ToArray()
                : DefaultColumns.ToArray();
            ValidateNoControlSeparator(columns);
            var explicitWidths = NormalizeExplicitWidths(export.ColumnWidths, maxWidth);

            if (request.Target is FileStream fileTarget && !string.IsNullOrWhiteSpace(fileTarget.Name))
            {
                var destinationPath = Path.GetFullPath(fileTarget.Name);
                var directory = Path.GetDirectoryName(destinationPath) ?? Directory.GetCurrentDirectory();
                var tempPath = Path.Combine(directory, $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
                try
                {
                    using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await WriteContentAsync(output, export, columns, explicitWidths, maxWidth, sampleLimit,
                            encoding, culture, request.Progress, cancellationToken);
                    }

                    try
                    {
                        // Atomic sibling replacement when the destination is not locked.
                        File.Move(tempPath, destinationPath, overwrite: true);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        // The host holds the destination open (for example CLI convert streams
                        // into the file it already created): stream the completed temp file
                        // through the provided target instead, then clean up.
                        if (request.Target.CanSeek)
                        {
                            request.Target.SetLength(0);
                            request.Target.Position = 0;
                        }

                        using var completed = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        await completed.CopyToAsync(request.Target, cancellationToken);
                    }
                }
                finally
                {
                    try
                    {
                        if (File.Exists(tempPath))
                        {
                            File.Delete(tempPath);
                        }
                    }
                    catch
                    {
                    }
                }

                await request.Target.FlushAsync(cancellationToken);
            }
            else
            {
                await WriteContentAsync(request.Target, export, columns, explicitWidths, maxWidth, sampleLimit,
                    encoding, culture, request.Progress, cancellationToken);
                await request.Target.FlushAsync(cancellationToken);
            }

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

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new FileFormatReadResult
        { Success = false, Error = "Catalog list text export is write-only." });
    }

    private static async Task WriteContentAsync(
        Stream target,
        PlainTextListExportRequest export,
        string[] columns,
        Dictionary<string, int> explicitWidths,
        int maxWidth,
        int sampleLimit,
        Encoding encoding,
        CultureInfo? culture,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var formatter = new FieldFormatter(culture);
        using var writer = new StreamWriter(target, encoding, leaveOpen: true)
        {
            NewLine = "\r\n"
        };

        if (export.Layout == PlainTextListLayout.TabDelimited)
        {
            await WriteTabDelimitedAsync(writer, export, columns, formatter, progress, cancellationToken);
            await writer.FlushAsync(cancellationToken);
            return;
        }

        var measured = MeasureWidths(export.Rows, columns, formatter, explicitWidths, sampleLimit, cancellationToken);
        var widths = new int[columns.Length];
        for (var i = 0; i < columns.Length; i++)
        {
            widths[i] = explicitWidths.TryGetValue(columns[i], out var configured)
                ? configured
                : Math.Max(1, Math.Min(maxWidth, Math.Max(columns[i].Length, measured[i])));
        }

        if (export.IncludeHeader)
        {
            await writer.WriteLineAsync(JoinFixed(columns.Select((column, i) => Pad(Truncate(column, widths[i]), widths[i], rightAlign: false))));
            await writer.WriteLineAsync(JoinFixed(widths.Select(width => new string('-', Math.Max(1, width)))));
        }

        await WriteFixedRowsAsync(writer, export, columns, formatter, widths, progress, cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }

    private static async Task WriteTabDelimitedAsync(
        StreamWriter writer,
        PlainTextListExportRequest export,
        string[] columns,
        FieldFormatter formatter,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        if (export.IncludeHeader)
        {
            await writer.WriteLineAsync(string.Join('\t', columns));
        }

        var written = 0;
        string? group = null;
        var total = export.Rows is ICollection<Dictionary<string, object?>> sized ? sized.Count : (int?)null;
        foreach (var row in export.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (export.GroupBy is not null)
            {
                var key = ResolveValue(row, export.GroupBy) is { } raw
                    ? formatter.FormatPlain(raw, export.GroupBy) ?? string.Empty
                    : string.Empty;
                if (group is not null && !string.Equals(group, key, StringComparison.Ordinal))
                {
                    await writer.WriteLineAsync(string.Empty);
                }

                group = key;
            }

            await writer.WriteLineAsync(string.Join('\t',
                columns.Select(column => formatter.FormatPlain(ResolveValue(row, column), column) ?? string.Empty)));
            written++;
            if (written % 64 == 0 || (total.HasValue && written == total.Value))
            {
                progress?.Report(total.HasValue && total.Value > 0
                    ? Math.Clamp(written * 100 / total.Value, 0, 100)
                    : Math.Min(99, written / 64));
            }
        }
    }

    private static async Task WriteFixedRowsAsync(
        StreamWriter writer,
        PlainTextListExportRequest export,
        string[] columns,
        FieldFormatter formatter,
        int[] widths,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var written = 0;
        string? group = null;
        var rows = export.Rows as IReadOnlyList<Dictionary<string, object?>>;
        var total = rows?.Count ?? (export.Rows as ICollection<Dictionary<string, object?>>)?.Count;

        if (rows is not null)
        {
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                group = await WriteFixedRowAsync(writer, row, columns, formatter, widths, export, group);
                written++;
                ReportProgress(progress, total, written);
            }

            return;
        }

        using var enumerator = export.Rows.GetEnumerator();
        var buffered = new List<Dictionary<string, object?>>();
        while (buffered.Count < export.MaxWidthSampleRows && enumerator.MoveNext())
        {
            cancellationToken.ThrowIfCancellationRequested();
            buffered.Add(enumerator.Current);
        }

        foreach (var row in buffered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            group = await WriteFixedRowAsync(writer, row, columns, formatter, widths, export, group);
            written++;
            ReportProgress(progress, total, written);
        }

        while (enumerator.MoveNext())
        {
            cancellationToken.ThrowIfCancellationRequested();
            group = await WriteFixedRowAsync(writer, enumerator.Current, columns, formatter, widths, export, group);
            written++;
            ReportProgress(progress, total, written);
        }
    }

    private static void ReportProgress(IProgress<int>? progress, int? total, int written)
    {
        if (written % 64 != 0 && (!total.HasValue || written != total.Value))
        {
            return;
        }

        progress?.Report(total.HasValue && total.Value > 0
            ? Math.Clamp(written * 100 / total.Value, 0, 100)
            : Math.Min(99, written / 64));
    }

    private static async Task<string?> WriteFixedRowAsync(
        StreamWriter writer,
        Dictionary<string, object?> row,
        string[] columns,
        FieldFormatter formatter,
        int[] widths,
        PlainTextListExportRequest export,
        string? group)
    {
        if (export.GroupBy is not null)
        {
            var key = formatter.FormatPlain(ResolveValue(row, export.GroupBy), export.GroupBy) ?? string.Empty;
            if (group is not null && !string.Equals(group, key, StringComparison.Ordinal))
            {
                await writer.WriteLineAsync(string.Empty);
            }

            group = key;
        }

        var fields = new string[columns.Length];
        for (var i = 0; i < columns.Length; i++)
        {
            fields[i] = Pad(
                Truncate(formatter.FormatPlain(ResolveValue(row, columns[i]), columns[i]) ?? string.Empty, widths[i]),
                widths[i],
                rightAlign: columns[i].Equals("SizeBytes", StringComparison.OrdinalIgnoreCase));
        }

        await writer.WriteLineAsync(JoinFixed(fields));
        return group;
    }

    private static int[] MeasureWidths(
        IEnumerable<Dictionary<string, object?>> rows,
        string[] columns,
        FieldFormatter formatter,
        Dictionary<string, int> explicitWidths,
        int sampleLimit,
        CancellationToken cancellationToken)
    {
        var widths = new int[columns.Length];
        if (rows is IReadOnlyList<Dictionary<string, object?>> list)
        {
            foreach (var row in list)
            {
                cancellationToken.ThrowIfCancellationRequested();
                MeasureRow(widths, columns, formatter, explicitWidths, row);
            }

            return widths;
        }

        var sampled = 0;
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sampled >= sampleLimit)
            {
                break;
            }

            MeasureRow(widths, columns, formatter, explicitWidths, row);
            sampled++;
        }

        return widths;
    }

    private static void MeasureRow(
        int[] widths,
        string[] columns,
        FieldFormatter formatter,
        Dictionary<string, int> explicitWidths,
        Dictionary<string, object?> row)
    {
        for (var i = 0; i < columns.Length; i++)
        {
            if (explicitWidths.ContainsKey(columns[i]))
            {
                continue;
            }

            var text = formatter.FormatPlain(ResolveValue(row, columns[i]), columns[i]) ?? string.Empty;
            widths[i] = Math.Max(widths[i], text.Length);
        }
    }

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

    private static string Pad(string text, int width, bool rightAlign) =>
        rightAlign ? text.PadLeft(width) : text.PadRight(width);

    private static string JoinFixed(IEnumerable<string> fields) =>
        string.Join("  ", fields);

    private static string Truncate(string value, int maxWidth)
    {
        if (value.Length <= maxWidth)
        {
            return value;
        }

        return value[..Math.Max(0, maxWidth - 1)] + "…";
    }

    private static PlainTextListExportRequest NormalizeRequest(object? payload)
    {
        if (payload is PlainTextListExportRequest request)
        {
            ArgumentNullException.ThrowIfNull(request.Rows);
            return request;
        }

        return new PlainTextListExportRequest { Rows = CoerceRows(payload) };
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
                    : throw new PlainTextListExportException("JSON array items must be objects."));
        }

        throw new PlainTextListExportException("Payload must be catalog rows or a JSON array of objects.");
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

    private static void ValidateNoControlSeparator(string[] columns)
    {
        foreach (var column in columns)
        {
            if (column.IndexOfAny(['\r', '\n', '\t']) >= 0)
            {
                throw new PlainTextListExportException($"Column name '{column}' must not contain line breaks or tabs.");
            }
        }
    }

    private static int ValidateColumnWidth(int value, string name)
    {
        if (value is < 2 or > 10000)
        {
            throw new PlainTextListExportException($"{name} must be between 2 and 10000 characters.");
        }

        return value;
    }

    private static Dictionary<string, int> NormalizeExplicitWidths(
        IReadOnlyDictionary<string, int>? explicitWidths, int maxWidth)
    {
        var normalized = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (explicitWidths is null)
        {
            return normalized;
        }

        foreach (var (name, width) in explicitWidths)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new PlainTextListExportException("Explicit column widths require a column name.");
            }

            normalized[name] = ValidateColumnWidth(width, $"Width for column '{name}'");
            if (width > maxWidth)
            {
                throw new PlainTextListExportException(
                    $"Width for column '{name}' exceeds the maximum column width of {maxWidth}.");
            }
        }

        return normalized;
    }

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
            _ => throw new PlainTextListExportException($"Unsupported text encoding '{name}'.")
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
            throw new PlainTextListExportException($"Code page {codePage} is not available on this platform.");
        }
    }

    private static CultureInfo? ResolveCulture(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        try
        {
            return CultureInfo.GetCultureInfo(name.Trim());
        }
        catch (CultureNotFoundException)
        {
            throw new PlainTextListExportException($"Unknown culture '{name}'.");
        }
    }

    private sealed class FieldFormatter(CultureInfo? culture)
    {
        public string? FormatPlain(object? value, string column)
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
            return text is null ? null : NormalizeControls(text);
        }

        private string? FormatSize(object? value)
        {
            if (value is null)
            {
                return null;
            }

            if (value is long integer)
            {
                return culture is null
                    ? integer.ToString(CultureInfo.InvariantCulture)
                    : integer.ToString("N0", culture);
            }

            if (value is int small)
            {
                return culture is null
                    ? small.ToString(CultureInfo.InvariantCulture)
                    : small.ToString("N0", culture);
            }

            if (value is double number)
            {
                return culture is null
                    ? number.ToString("R", CultureInfo.InvariantCulture)
                    : number.ToString("N", culture);
            }

            var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLong))
            {
                return culture is null
                    ? parsedLong.ToString(CultureInfo.InvariantCulture)
                    : parsedLong.ToString("N0", culture);
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDouble))
            {
                return culture is null
                    ? parsedDouble.ToString("R", CultureInfo.InvariantCulture)
                    : parsedDouble.ToString("N", culture);
            }

            return text;
        }

        private string? FormatTimestamp(object? value)
        {
            switch (value)
            {
                case null:
                    return null;
                case DateTimeOffset offset:
                    return culture is null
                        ? offset.ToString("o", CultureInfo.InvariantCulture)
                        : offset.ToString(culture);
                case DateTime dateTime:
                    var zoned = dateTime.Kind == DateTimeKind.Unspecified
                        ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
                        : dateTime;
                    return culture is null
                        ? new DateTimeOffset(zoned).ToString("o", CultureInfo.InvariantCulture)
                        : zoned.ToString(culture);
                default:
                    return FormatTimestampText(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
        }

        private string? FormatTimestampText(string? text)
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
                return culture is null
                    ? parsed.ToString("o", CultureInfo.InvariantCulture)
                    : parsed.ToString(culture);
            }

            return trimmed;
        }

        private string? ToDisplayText(object? value) => value switch
        {
            null => null,
            string text => text,
            bool flag => flag ? bool.TrueString : bool.FalseString,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };

        private static string NormalizeControls(string text)
        {
            if (text.IndexOfAny(['\r', '\n', '\t']) < 0)
            {
                return text;
            }

            var builder = new StringBuilder(text.Length);
            foreach (var character in text)
            {
                builder.Append(character is '\r' or '\n' or '\t' ? ' ' : character);
            }

            return builder.ToString();
        }
    }
}
