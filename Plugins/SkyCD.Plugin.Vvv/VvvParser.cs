using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Csv;
using SkyCD.Plugin.Vvv.Exceptions;

namespace SkyCD.Plugin.Vvv;

/// <summary>
/// Parser for VVV (Virtual Volumes View) CSV/TSV exports. VVV writes one row
/// per file with all string fields always double-quoted (<c>"</c> escaped as
/// <c>""</c>), sizes and dates unquoted, and one of three separators (comma,
/// tab, pipe). Physical exports have seven columns
/// (Volume, Path, Name, Size, Ext, Last modified, Description); virtual-folder
/// exports have six (Name, Size, Ext, Last modified, PhysicalPath,
/// Description). Headers are optional. Column shapes verified against the VVV
/// export source (exportdata.cpp).
/// </summary>
public static class VvvParser
{
    public const int PhysicalColumnCount = 7;
    public const int VirtualColumnCount = 6;

    private static readonly char[] CandidateDelimiters = [',', '\t', '|'];

    private static readonly string[] PhysicalHeaders =
    [
        "Volume", "Path", "Name", "Size", "Ext", "Last modified", "Description"
    ];

    private static readonly string[] VirtualHeaders =
    [
        "Name", "Size", "Ext", "Last modified", "PhysicalPath", "Description"
    ];

    private const int SizeIndexPhysical = 3;
    private const int SizeIndexVirtual = 1;

    public static async Task<VvvParseResult> ParseAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        string text;
        try
        {
            using var reader = new StreamReader(
                source,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096,
                leaveOpen: true);
            text = await reader.ReadToEndAsync(cancellationToken);
        }
        catch (DecoderFallbackException exception)
        {
            throw new VvvMalformedException($"input is not valid UTF-8: {exception.Message}");
        }
        catch (IOException exception)
        {
            throw new VvvMalformedException($"input cannot be read: {exception.Message}");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new VvvMalformedException("input is empty.");
        }

        var winner = await ResolveDelimiterAsync(text, cancellationToken);
        var scope = winner.ColumnCount == PhysicalColumnCount ? VvvExportScope.Physical : VvvExportScope.Virtual;

        var header = DetectHeader(winner.Rows, scope, out var headerWarning);
        var warnings = new List<string>();
        if (headerWarning is not null)
        {
            warnings.Add(headerWarning);
        }

        var dataRows = header.HasHeader ? winner.Rows.Skip(1).ToList() : winner.Rows;
        var entries = new List<VvvFileEntry>();
        var skippedReasons = new List<string>();
        var skippedCount = 0;

        foreach (var row in dataRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryMapRow(row, scope, entries.Count, out var entry, out var reason))
            {
                entries.Add(entry!);
            }
            else
            {
                skippedCount++;
                skippedReasons.Add(reason!);
            }
        }

        if (entries.Count == 0)
        {
            throw new VvvEmptyReportException();
        }

        return new VvvParseResult(
            scope,
            winner.Delimiter,
            header.HasHeader,
            entries,
            skippedCount,
            skippedReasons,
            warnings);
    }

    private sealed record DelimiterWin(char Delimiter, int ColumnCount, List<List<string>> Rows);

    private static async Task<DelimiterWin> ResolveDelimiterAsync(string text, CancellationToken cancellationToken)
    {
        var wins = new List<DelimiterWin>();
        foreach (var delimiter in CandidateDelimiters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<List<string>> rows;
            try
            {
                rows = await CsvParser.ReadAllRowsAsync(new StringReader(text), delimiter, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException)
            {
                continue;
            }

            if (rows.Count == 0)
            {
                continue;
            }

            var columnCount = rows[0].Count;
            if (columnCount != PhysicalColumnCount && columnCount != VirtualColumnCount)
            {
                continue;
            }

            if (rows.Any(row => row.Count != columnCount))
            {
                continue;
            }

            wins.Add(new DelimiterWin(delimiter, columnCount, rows));
        }

        if (wins.Count == 0)
        {
            throw new VvvAmbiguousFormatException(
                "no separator (comma, tab, pipe) yields uniform 6- or 7-column rows. " +
                "Expected a VVV physical export (Volume, Path, Name, Size, Ext, Last modified, Description) " +
                "or virtual export (Name, Size, Ext, Last modified, PhysicalPath, Description).");
        }

        if (wins.Count == 1)
        {
            return wins[0];
        }

        var headerWin = wins.FirstOrDefault(win => MatchesHeaders(win.Rows[0], win.ColumnCount));
        return headerWin ?? wins[0];
    }

    private static bool MatchesHeaders(IReadOnlyList<string> firstRow, int columnCount)
    {
        var expected = columnCount == PhysicalColumnCount ? PhysicalHeaders : VirtualHeaders;
        if (firstRow.Count != expected.Length)
        {
            return false;
        }

        for (var i = 0; i < expected.Length; i++)
        {
            if (!firstRow[i].Equals(expected[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private sealed record HeaderDecision(bool HasHeader);

    private static HeaderDecision DetectHeader(
        List<List<string>> rows, VvvExportScope scope, out string? warning)
    {
        warning = null;
        var first = rows[0];
        if (MatchesHeaders(first, first.Count))
        {
            return new HeaderDecision(true);
        }

        // Headerless exports start with data: the size column parses as a number.
        // Localized headers do not, so a non-numeric size field with numeric sizes
        // below still indicates a header row of an unknown locale.
        var sizeIndex = scope == VvvExportScope.Physical ? SizeIndexPhysical : SizeIndexVirtual;
        if (!TryParseSize(first[sizeIndex], out _)
            && rows.Skip(1).Any(row => TryParseSize(row[sizeIndex], out _)))
        {
            warning = "First row does not match the known English headers and is treated as a localized header row.";
            return new HeaderDecision(true);
        }

        return new HeaderDecision(false);
    }

    private static bool TryMapRow(
        List<string> row,
        VvvExportScope scope,
        int entryIndex,
        out VvvFileEntry? entry,
        out string? reason)
    {
        entry = null;
        reason = null;

        string volume;
        string path;
        string name;
        string sizeText;
        string extension;
        string lastModified;
        string description;
        string? physicalPath;

        if (scope == VvvExportScope.Physical)
        {
            volume = row[0].Trim();
            path = NormalizeRelativePath(row[1]);
            name = row[2].Trim();
            sizeText = row[3];
            extension = row[4].Trim();
            lastModified = row[5].Trim();
            description = row[6];
            physicalPath = null;

            if (string.IsNullOrWhiteSpace(volume))
            {
                reason = $"Row {entryIndex + 1} skipped: physical entry has no volume name.";
                return false;
            }
        }
        else
        {
            volume = string.Empty;
            name = row[0].Trim();
            sizeText = row[1];
            extension = row[2].Trim();
            lastModified = row[3].Trim();
            physicalPath = row[4].Trim();
            description = row[5];
            path = NormalizeParentPath(physicalPath);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            reason = $"Row {entryIndex + 1} skipped: entry has no file name.";
            return false;
        }

        if (!TryParseSize(sizeText, out var size))
        {
            reason = $"Row {entryIndex + 1} skipped: size '{sizeText.Trim()}' is not a number.";
            return false;
        }

        entry = new VvvFileEntry(volume, path, name, size, extension, lastModified, description, physicalPath);
        return true;
    }

    private static bool TryParseSize(string text, out long size) =>
        long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out size) && size >= 0;

    private static string NormalizeRelativePath(string path)
    {
        var normalized = path.Replace('\\', '/').Trim().Trim('/');
        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        return normalized;
    }

    private static string NormalizeParentPath(string physicalPath)
    {
        var normalized = physicalPath.Replace('\\', '/').Trim().TrimEnd('/');
        var separator = normalized.LastIndexOf('/');
        if (separator <= 0)
        {
            return string.Empty;
        }

        return normalized[..separator].Trim('/');
    }
}
