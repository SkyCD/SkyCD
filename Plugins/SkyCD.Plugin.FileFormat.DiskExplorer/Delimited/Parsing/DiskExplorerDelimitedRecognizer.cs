using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SkyCD.Plugin.FileFormat.DiskExplorer.Delimited.Models;
using SkyCD.Plugin.FileFormat.DiskExplorer.Models;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Delimited.Parsing;

/// <summary>
/// Content-aware recognizer for the Disk Explorer delimited-text profile.
/// Requires a header row with at least two recognized column labels and
/// uniform column counts on sampled rows. VVV-shaped exports (PhysicalPath,
/// Last modified, Ext headers) and XML/HTML reports are never claimed.
/// The scan is bounded.
/// </summary>
public static class DiskExplorerDelimitedRecognizer
{
    private static readonly char[] CandidateSeparators = [',', '\t', ';', '|'];

    private static readonly string[] VetoHeaders = ["PhysicalPath", "Last modified", "Ext"];

    private const int MaxProbeLines = 50;
    private const int MaxProbeChars = 32768;
    private const int MinKnownHeaders = 2;

    /// <summary>
    /// Inspects the stream content and returns true when it looks like a Disk
    /// Explorer delimited-text report. The stream position is preserved.
    /// </summary>
    public static bool IsDiskExplorerDelimited(Stream stream)
    {
        if (!stream.CanRead || !stream.CanSeek)
        {
            return false;
        }

        var initialPosition = stream.Position;
        try
        {
            var position = stream.Position;
            var preamble = new byte[3];
            var read = stream.Read(preamble, 0, preamble.Length);
            if (read == 3 && preamble[0] == 0xEF && preamble[1] == 0xBB && preamble[2] == 0xBF)
            {
                position += 3;
            }

            stream.Position = position;
            using var reader = new StreamReader(stream, CodePagesEncodingProvider.Instance.GetEncoding(1252)!, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

            var lines = new List<string>();
            var chars = 0;
            while (lines.Count < MaxProbeLines)
            {
                var logical = ReadLogicalProbeRow(reader, ref chars);
                if (logical is null)
                {
                    break;
                }

                if (chars > MaxProbeChars)
                {
                    break;
                }

                if (!string.IsNullOrWhiteSpace(logical))
                {
                    lines.Add(logical);
                }
            }

            if (lines.Count == 0)
            {
                return false;
            }

            foreach (var separator in CandidateSeparators)
            {
                if (MatchesHeader(lines, separator))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            stream.Position = initialPosition;
        }
    }

    private static bool MatchesHeader(List<string> lines, char separator)
    {
        var header = SplitSimple(lines[0], separator);
        if (header.Count < 2)
        {
            return false;
        }

        // VVV-shaped exports share some labels; their distinctive headers veto the match.
        foreach (var label in header)
        {
            var trimmed = label.Trim().Trim('"');
            foreach (var veto in VetoHeaders)
            {
                if (trimmed.Equals(veto, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        var knownHits = 0;
        foreach (var label in header)
        {
            var trimmed = label.Trim().Trim('"');
            if (DiskExplorerNames.MapField(trimmed) != DiskExplorerField.Unknown
                || DiskExplorerDelimitedColumns.IsStructural(trimmed))
            {
                knownHits++;
            }
        }

        if (knownHits < MinKnownHeaders)
        {
            return false;
        }

        // Only the header plus the first data row must agree: deeper
        // inconsistencies are reported precisely by the parser instead.
        var probeRows = Math.Min(lines.Count, 2);
        for (var i = 1; i < probeRows; i++)
        {
            if (SplitSimple(lines[i], separator).Count != header.Count)
            {
                return false;
            }
        }

        return true;
    }

    private static string? ReadLogicalProbeRow(StreamReader reader, ref int chars)
    {
        var builder = new StringBuilder();
        var inQuotes = false;
        var fieldStart = true;
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            foreach (var ch in line)
            {
                if (++chars > MaxProbeChars * 4)
                {
                    return builder.Length == 0 ? null : builder.ToString();
                }

                if (ch == '"' && fieldStart)
                {
                    inQuotes = true;
                    fieldStart = false;
                    builder.Append(ch);
                    continue;
                }

                if (ch == '"' && inQuotes)
                {
                    inQuotes = false;
                    fieldStart = false;
                    builder.Append(ch);
                    continue;
                }

                if ((ch == ',' || ch == '\t' || ch == ';' || ch == '|') && !inQuotes)
                {
                    fieldStart = true;
                }
                else if (!inQuotes)
                {
                    fieldStart = false;
                }

                builder.Append(ch);
            }

            if (!inQuotes)
            {
                return builder.ToString();
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static List<string> SplitSimple(string line, char separator)
    {
        var fields = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;
        var fieldStart = true;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"' && fieldStart)
            {
                inQuotes = true;
                fieldStart = false;
                continue;
            }

            if (ch == '"' && inQuotes)
            {
                if (i + 1 < line.Length && line[i + 1] == '"')
                {
                    builder.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = false;
                }

                fieldStart = false;
                continue;
            }

            if (ch == separator && !inQuotes)
            {
                fields.Add(builder.ToString());
                builder.Clear();
                fieldStart = true;
                continue;
            }

            builder.Append(ch);
            fieldStart = false;
        }

        fields.Add(builder.ToString());
        return fields;
    }
}
