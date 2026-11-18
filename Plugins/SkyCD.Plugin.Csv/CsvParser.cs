using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;

namespace SkyCD.Plugin.Csv;

/// <summary>
/// Shared CSV stream parser supporting auto-detected delimiters, quoted
/// delimiters/newlines, and RFC 4180 quote escaping. The public API is
/// implemented via CsvHelper where appropriate; character-level helpers
/// remain for callers that operate on individual lines (<see cref="CsvCatalogPlugin"/>).
/// </summary>
public static class CsvParser
{
    /// <summary>
    /// Detects the column separator delimiter from a header line by counting
    /// unquoted occurrences of comma, semicolon and tab, returning the most
    /// frequent (comma on a tie).
    /// </summary>
    public static char DetectDelimiter(string headerLine)
    {
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            return ',';
        }

        var commaCount = 0;
        var semicolonCount = 0;
        var tabCount = 0;
        var inQuotes = false;

        for (var index = 0; index < headerLine.Length; index++)
        {
            var current = headerLine[index];
            if (current == '"')
            {
                if (inQuotes && index + 1 < headerLine.Length && headerLine[index + 1] == '"')
                {
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (!inQuotes)
            {
                if (current == ',') commaCount++;
                else if (current == ';') semicolonCount++;
                else if (current == '\t') tabCount++;
            }
        }

        if (semicolonCount > commaCount && semicolonCount > tabCount) return ';';
        if (tabCount > commaCount && tabCount > semicolonCount) return '\t';
        return ',';
    }

    /// <summary>
    /// Parses a single CSV line with the given delimiter using CsvHelper.
    /// </summary>
    public static List<string> ParseLine(string line, char delimiter = ',')
    {
        var options = new CsvReaderOptions
        {
            Separator = delimiter,
            HasHeader = false,
            SkipEmptyLines = false,
            MalformedData = CsvMalformedDataHandling.Recover,
            AllowLineBreaksInQuotes = false,
            Mode = CsvMode.Rfc4180
        };

        using var reader = CsvReader.Create(new StringReader(line), options, leaveOpen: false);
        if (reader.Read())
        {
            var fields = new List<string>(reader.FieldCount);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                fields.Add(reader.GetString(i));
            }

            return fields;
        }

        return [];
    }

    /// <summary>
    /// Reads all records from a text reader with detected delimiter, supporting
    /// multi-line quoted fields. Blank lines between records are skipped.
    /// </summary>
    public static async Task<CsvReadResult> ReadCsvAsync(
        TextReader reader,
        char? explicitDelimiter = null,
        CancellationToken cancellationToken = default)
    {
        // Buffer the entire content so we can detect the delimiter from the
        // first non-blank line before handing it to CsvHelper.
        // Preserve the LF line endings returned by the previous line-based parser.
        var content = (await reader.ReadToEndAsync(cancellationToken))
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        var delimiter = explicitDelimiter ?? ',';
        if (!explicitDelimiter.HasValue)
        {
            foreach (var candidate in content.Split('\n'))
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    delimiter = DetectDelimiter(candidate);
                    break;
                }
            }
        }

        var options = new CsvReaderOptions
        {
            Separator = delimiter,
            HasHeader = false,
            SkipEmptyLines = true,
            MalformedData = CsvMalformedDataHandling.Recover,
            AllowLineBreaksInQuotes = true,
            Mode = CsvMode.Rfc4180
        };

        var rows = new List<List<string>>();
        using var csvReader = CsvReader.Create(new StringReader(content), options, leaveOpen: false);

        while (await csvReader.ReadAsync(cancellationToken))
        {
            var row = new List<string>(csvReader.FieldCount);
            for (var i = 0; i < csvReader.FieldCount; i++)
            {
                row.Add(csvReader.GetString(i));
            }

            // Skip purely empty rows (single whitespace-only field).
            if (row.Count == 1 && string.IsNullOrWhiteSpace(row[0]))
            {
                continue;
            }

            rows.Add(row);
        }

        return new CsvReadResult(rows, delimiter);
    }

    /// <summary>
    /// Reads all records from a text reader, supporting multi-line quoted fields.
    /// </summary>
    public static async Task<List<List<string>>> ReadAllRowsAsync(
        TextReader reader,
        char? explicitDelimiter = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ReadCsvAsync(reader, explicitDelimiter, cancellationToken);
        return result.Rows;
    }

    /// <summary>
    /// Escapes a field value for CSV serialization per RFC 4180.
    /// </summary>
    public static string Escape(string? value, char delimiter = ',')
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        return escaped.Contains(delimiter) || escaped.Contains('"') || escaped.Contains('\n') || escaped.Contains('\r')
            ? $"\"{escaped}\""
            : escaped;
    }
}

public sealed record CsvReadResult(List<List<string>> Rows, char Delimiter);
