using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Csv;

/// <summary>
/// Shared, compliant CSV stream parser supporting auto-detected delimiters,
/// quoted delimiters/newlines, and RFC 4180 quote escaping.
/// </summary>
public static class CsvParser
{
    private static readonly char[] RecognizedDelimiters = [',', ';', '\t'];

    /// <summary>
    /// Detects the column separator delimiter from a header line.
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
    /// Parses a single CSV line with the given delimiter.
    /// </summary>
    public static List<string> ParseLine(string line, char delimiter = ',')
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
            else if (current == delimiter && !inQuotes)
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

    /// <summary>
    /// Reads all records from a text reader with detected delimiter, supporting multi-line quoted fields.
    /// </summary>
    public static async Task<CsvReadResult> ReadCsvAsync(
        TextReader reader,
        char? explicitDelimiter = null,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<List<string>>();
        var currentRow = new List<string>();
        var fieldBuffer = new StringBuilder();
        var inQuotes = false;
        var delimiter = explicitDelimiter ?? ',';
        var delimiterResolved = explicitDelimiter.HasValue;
        var hasContentInField = false;

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (!delimiterResolved && !inQuotes)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                delimiter = DetectDelimiter(line);
                delimiterResolved = true;
            }

            for (var index = 0; index < line.Length; index++)
            {
                var current = line[index];
                if (current == '"')
                {
                    if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                    {
                        fieldBuffer.Append('"');
                        hasContentInField = true;
                        index++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                        hasContentInField = true;
                    }
                }
                else if (current == delimiter && !inQuotes)
                {
                    currentRow.Add(fieldBuffer.ToString());
                    fieldBuffer.Clear();
                    hasContentInField = false;
                }
                else
                {
                    fieldBuffer.Append(current);
                    hasContentInField = true;
                }
            }

            if (inQuotes)
            {
                // Newline within a quoted field
                fieldBuffer.Append('\n');
            }
            else
            {
                currentRow.Add(fieldBuffer.ToString());
                fieldBuffer.Clear();

                // Skip purely empty lines between records
                if (currentRow.Count > 1 || (currentRow.Count == 1 && !string.IsNullOrWhiteSpace(currentRow[0])))
                {
                    rows.Add(currentRow);
                }

                currentRow = new List<string>();
                hasContentInField = false;
            }
        }

        // Handle case where file ended while still in quotes or with pending fields
        if (inQuotes || hasContentInField || currentRow.Count > 0)
        {
            currentRow.Add(fieldBuffer.ToString());
            if (currentRow.Count > 1 || (currentRow.Count == 1 && !string.IsNullOrWhiteSpace(currentRow[0])))
            {
                rows.Add(currentRow);
            }
        }

        return new CsvReadResult(rows, delimiter);
    }

    /// <summary>
    /// Reads all records from a text reader, supporting multi-line quoted fields.

    public static async Task<List<List<string>>> ReadAllRowsAsync(
        TextReader reader,
        char? explicitDelimiter = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ReadCsvAsync(reader, explicitDelimiter, cancellationToken);
        return result.Rows;
    }

    /// <summary>
    /// Escapes a field value for CSV serialization.
    /// </summary>
    public static string Escape(string? value, char delimiter = ',')
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var escaped = value.Replace("\"", "\"\"");
        return escaped.Contains(delimiter) || escaped.Contains('"') || escaped.Contains('\n') || escaped.Contains('\r')
            ? $"\"{escaped}\""
            : escaped;
    }
}

public sealed record CsvReadResult(List<List<string>> Rows, char Delimiter);
