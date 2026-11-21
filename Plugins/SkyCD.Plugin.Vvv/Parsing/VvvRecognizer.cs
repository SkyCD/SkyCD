using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Csv;
using SkyCD.Plugin.Csv.Parsing;

namespace SkyCD.Plugin.Vvv.Parsing;

/// <summary>
/// Content-aware recognizer for VVV exports. Recognition requires a uniform
/// 6- or 7-column layout under a VVV separator whose first row matches the
/// English VVV header vocabulary, so headerless exports and generic CSV
/// files are never auto-claimed (they remain importable by explicit format
/// selection). The scan is bounded.
/// </summary>
public static class VvvRecognizer
{
    private static readonly char[] CandidateDelimiters = [',', '\t', '|'];

    private static readonly string[] PhysicalHeaders =
    [
        "Volume", "Path", "Name", "Size", "Ext", "Last modified", "Description"
    ];

    private static readonly string[] VirtualHeaders =
    [
        "Name", "Size", "Ext", "Last modified", "PhysicalPath", "Description"
    ];

    private const int MaxProbeLines = 50;
    private const int MaxProbeBytes = 32768;

    /// <summary>
    /// Inspects the stream content and returns true when it looks like a VVV
    /// export with headers. The stream position is preserved.
    /// </summary>
    public static bool IsVvvExport(Stream stream)
    {
        if (!stream.CanRead)
        {
            return false;
        }

        var initialPosition = stream.CanSeek ? stream.Position : -1;
        try
        {
            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096,
                leaveOpen: true);
            var probe = ReadProbe(reader);
            return ProbeCandidates(probe);
        }
        catch
        {
            return false;
        }
        finally
        {
            if (initialPosition >= 0 && stream.CanSeek)
            {
                stream.Position = initialPosition;
            }
        }
    }

    private static bool ProbeCandidates(System.Collections.Generic.List<string> probe)
    {
        foreach (var delimiter in CandidateDelimiters)
        {
            var rows = probe
                .Where(static line => !string.IsNullOrWhiteSpace(line))
                .Select(line => CsvParser.ParseLine(line, delimiter))
                .ToList();
            if (rows.Count == 0)
            {
                continue;
            }

            var columnCount = rows[0].Count;
            if (columnCount != VvvParser.PhysicalColumnCount && columnCount != VvvParser.VirtualColumnCount)
            {
                continue;
            }

            if (rows.Any(row => row.Count != columnCount))
            {
                continue;
            }

            var expected = columnCount == VvvParser.PhysicalColumnCount ? PhysicalHeaders : VirtualHeaders;
            if (expected.Where((header, index) => !rows[0][index].Equals(header, StringComparison.OrdinalIgnoreCase)).Any())
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static System.Collections.Generic.List<string> ReadProbe(StreamReader reader)
    {
        var lines = new System.Collections.Generic.List<string>();
        var bytes = 0;
        while (lines.Count < MaxProbeLines)
        {
            var line = reader.ReadLine();
            if (line is null)
            {
                break;
            }

            bytes += Encoding.UTF8.GetByteCount(line);
            if (bytes > MaxProbeBytes)
            {
                break;
            }

            lines.Add(line);
        }

        return lines;
    }
}
