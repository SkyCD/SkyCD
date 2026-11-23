using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using SkyCD.Plugin.EncodingProfiles;
using SkyCD.Plugin.FileFormat.DiskExplorer.Delimited.Exceptions;
using SkyCD.Plugin.FileFormat.DiskExplorer.Delimited.Import;
using SkyCD.Plugin.FileFormat.DiskExplorer.Delimited.Models;
using SkyCD.Plugin.FileFormat.DiskExplorer.Models;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Delimited.Parsing;

/// <summary>
/// Streaming parser for the Disk Explorer Professional "Special Export"
/// delimited-text profile. One row per entry; the first row is a header with
/// user-ordered column labels (case-insensitive). Separator auto-detection
/// covers comma, tab, semicolon, and pipe; any other single-character
/// separator is rejected as ambiguous. A double quote opens a quoted field
/// only at field start; elsewhere it is literal, so historical unquoted rows
/// parse identically. Quoted fields may span lines. Encoding is UTF-8 with
/// BOM when present, valid UTF-8 otherwise, and Windows-1252 as the
/// historical fallback (UTF-16 input is rejected explicitly). Parsed rows are
/// topologically reordered so parents precede children, then mapped onto the
/// shared <see cref="DiskExplorerParseResult"/> shape.
/// </summary>
public static class DiskExplorerDelimitedParser
{
    public const int MaxFieldChars = 1_048_576;
    public const int MaxRowChars = 8_388_608;
    public const long MaxRows = 10_000_000;

    private static readonly char[] CandidateSeparators = [',', '\t', ';', '|'];

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    public static DiskExplorerDelimitedImport Parse(Stream source, CancellationToken cancellationToken)
        => Parse(source, fileNameHint: null, cancellationToken);

    public static DiskExplorerDelimitedImport Parse(
        Stream source,
        string? fileNameHint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var encoding = DetectEncoding(source);
        using var reader = CreateReader(source, encoding);
        var header = ReadHeader(reader, fileNameHint, cancellationToken, out var headerLines);
        return ParseBody(reader, header, headerLines, cancellationToken);
    }

    private static DiskExplorerDelimitedImport ParseBody(
        StreamReader reader, HeaderInfo header, long headerLines, CancellationToken cancellationToken)
    {
        var records = new List<RowRecord>();
        var unsupportedFields = new List<string>();
        var unsupportedSeen = new HashSet<string>(StringComparer.Ordinal);
        var skippedReasons = new List<string>();
        const int skippedItemsCount = 0;
        long rowNumber = headerLines;
        long rowsRead = 0;

        string? line;
        while ((line = ReadLogicalRow(reader, header.Separator, ref rowNumber, cancellationToken)) is not null)
        {
            if (rowsRead >= MaxRows)
            {
                throw new DiskExplorerDelimitedMalformedException(
                    $"report exceeds the maximum supported row count of {MaxRows}.");
            }

            rowsRead++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = SplitRow(line, rowNumber);
            if (fields.Count != header.Columns.Count)
            {
                throw new DiskExplorerDelimitedMalformedException(
                    $"row {rowNumber} has {fields.Count} fields, expected {header.Columns.Count}.");
            }

            records.Add(MapRow(fields, header, unsupportedFields, unsupportedSeen));
        }

        if (records.Count == 0)
        {
            throw new DiskExplorerDelimitedEmptyReportException();
        }

        var syntheticCount = SynthesizeMissingHierarchy(records);
        var partial = ResolveParents(records, syntheticCount);
        var items = BuildItems(records);

        return new DiskExplorerDelimitedImport(
            new DiskExplorerParseResult(
                Title: null,
                Items: items,
                UnsupportedFields: unsupportedFields,
                SkippedItemsCount: skippedItemsCount,
                SkippedReasons: skippedReasons),
            IsPartial: partial);
    }

    private sealed record HeaderInfo(char Separator, List<ColumnMapping> Columns);

    private sealed record ColumnMapping(string Label, DiskExplorerField Field, bool Structural);

    private sealed record RowRecord(
        DiskExplorerElementKind Kind,
        string? RawKind,
        string? Volume,
        string? RelativePath,
        Dictionary<DiskExplorerField, string> Fields,
        Dictionary<string, string> Extra,
        int ParentPosition);

    private static Encoding DetectEncoding(Stream source)
    {
        if (!source.CanSeek)
        {
            throw new DiskExplorerDelimitedMalformedException(
                "source stream must be seekable for encoding detection.");
        }

        var position = source.Position;
        try
        {
            var preamble = new byte[4];
            var read = 0;
            while (read < preamble.Length)
            {
                var got = source.Read(preamble, read, preamble.Length - read);
                if (got == 0)
                {
                    break;
                }

                read += got;
            }

            if (read >= 3 && preamble[0] == Utf8Bom[0] && preamble[1] == Utf8Bom[1] && preamble[2] == Utf8Bom[2])
            {
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            }

            if (read >= 2
                && ((preamble[0] == 0xFF && preamble[1] == 0xFE) || (preamble[0] == 0xFE && preamble[1] == 0xFF)))
            {
                throw new DiskExplorerDelimitedMalformedException(
                    "UTF-16 input is not supported; save the export as UTF-8 (BOM optional) or Windows-1252.");
            }

            if (IsValidUtf8(source))
            {
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            }

            return LegacyEncoding.StrictWindows1252;
        }
        finally
        {
            source.Position = position;
        }
    }

    private static bool IsValidUtf8(Stream source)
    {
        var position = source.Position;
        try
        {
            source.Position = position;
            using var probe = new StreamReader(
                source,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096,
                leaveOpen: true);
            var buffer = new char[4096];
            while (!probe.EndOfStream)
            {
                probe.Read(buffer, 0, buffer.Length);
            }

            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
        finally
        {
            source.Position = position;
        }
    }

    private static HeaderInfo ReadHeader(
        StreamReader reader,
        string? fileNameHint,
        CancellationToken cancellationToken,
        out long headerLines)
    {
        long rowNumber = 0;
        string? headerLine = null;
        while (headerLine is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = reader.ReadLine();
            if (candidate is null)
            {
                throw new DiskExplorerDelimitedMalformedException("input is empty.");
            }

            rowNumber++;
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                headerLine = candidate;
            }
        }

        var wins = new List<(char Separator, List<string> Labels, int KnownHits)>();
        foreach (var separator in CandidateSeparators)
        {
            var labels = SplitPhysicalLine(headerLine, separator);
            if (labels.Count < 2)
            {
                continue;
            }

            var knownHits = 0;
            foreach (var label in labels)
            {
                var trimmed = label.Trim();
                if (DiskExplorerNames.MapField(trimmed) != DiskExplorerField.Unknown
                    || DiskExplorerDelimitedColumns.IsStructural(trimmed))
                {
                    knownHits++;
                }
            }

            if (knownHits > 0)
            {
                wins.Add((separator, labels, knownHits));
            }
        }

        if (wins.Count == 0)
        {
            throw new DiskExplorerDelimitedAmbiguousException(
                "no header row with recognized column labels found for separators comma, tab, semicolon, or pipe. " +
                "Expected labels such as NAME, SIZE, DATE, TIME, TYPE, DESC, PATH, VOLUME, ITEMTYPE.");
        }

        var best = wins[0];
        var tied = false;
        foreach (var win in wins)
        {
            if (win.Separator == best.Separator)
            {
                continue;
            }

            if (win.KnownHits > best.KnownHits)
            {
                best = win;
                tied = false;
            }
            else if (win.KnownHits == best.KnownHits)
            {
                tied = true;
            }
        }

        if (tied)
        {
            var hinted = ApplyExtensionHint(wins, fileNameHint);
            if (hinted is null)
            {
                throw new DiskExplorerDelimitedAmbiguousException(
                    "several separators fit the header; save with .tsv for tab or .csv for comma, or use an unambiguous dialect.");
            }

            best = hinted.Value;
        }

        var columns = new List<ColumnMapping>(best.Labels.Count);
        foreach (var label in best.Labels)
        {
            var trimmed = label.Trim();
            columns.Add(new ColumnMapping(
                trimmed,
                DiskExplorerNames.MapField(trimmed),
                DiskExplorerDelimitedColumns.IsStructural(trimmed)));
        }

        headerLines = rowNumber;
        return new HeaderInfo(best.Separator, columns);
    }

    private static StreamReader CreateReader(Stream source, Encoding encoding)
    {
        if (encoding is UTF8Encoding)
        {
            var position = source.Position;
            var preamble = new byte[3];
            var read = source.Read(preamble, 0, preamble.Length);
            if (!(read == 3 && preamble[0] == Utf8Bom[0] && preamble[1] == Utf8Bom[1] && preamble[2] == Utf8Bom[2]))
            {
                source.Position = position;
            }
        }

        return new StreamReader(source, encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
    }

    private static List<string> SplitPhysicalLine(string line, char separator)
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

    private static (char Separator, List<string> Labels, int KnownHits)? ApplyExtensionHint(
        List<(char Separator, List<string> Labels, int KnownHits)> wins, string? fileNameHint)
    {
        if (string.IsNullOrWhiteSpace(fileNameHint))
        {
            return null;
        }

        char? preferred = fileNameHint.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase) ? '\t'
            : fileNameHint.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? ','
            : null;
        if (preferred is null)
        {
            return null;
        }

        foreach (var win in wins)
        {
            if (win.Separator == preferred)
            {
                return win;
            }
        }

        return null;
    }

    private static string? ReadLogicalRow(
        StreamReader reader, char separator, ref long rowNumber, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var inQuotes = false;
        var fieldStart = true;
        var rowChars = 0;
        var startRow = rowNumber + 1;
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowNumber++;

            if (builder.Length > 0)
            {
                builder.Append('\n');
                rowChars++;
            }

            for (var i = 0; i < line.Length; i++)
            {
                if (rowChars >= MaxRowChars)
                {
                    throw new DiskExplorerDelimitedMalformedException(
                        $"row {startRow} exceeds the maximum supported length of {MaxRowChars} characters.");
                }

                var ch = line[i];
                rowChars++;

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
                    builder.Append('\0');
                    fieldStart = true;
                    continue;
                }

                builder.Append(ch);
                fieldStart = false;
            }

            if (!inQuotes)
            {
                return builder.ToString();
            }
        }

        if (builder.Length == 0)
        {
            return null;
        }

        throw new DiskExplorerDelimitedMalformedException($"row {startRow} has an unterminated quoted field.");
    }

    private static List<string> SplitRow(string logicalRow, long rowNumber)
    {
        var fields = new List<string>();
        var builder = new StringBuilder();
        var fieldChars = 0;
        foreach (var ch in logicalRow)
        {
            if (ch == '\0')
            {
                fields.Add(builder.ToString());
                builder.Clear();
                fieldChars = 0;
                continue;
            }

            if (fieldChars >= MaxFieldChars)
            {
                throw new DiskExplorerDelimitedMalformedException(
                    $"row {rowNumber} field exceeds the maximum supported length of {MaxFieldChars} characters.");
            }

            builder.Append(ch);
            fieldChars++;
        }

        fields.Add(builder.ToString());
        return fields;
    }

    private static RowRecord MapRow(
        List<string> fields,
        HeaderInfo header,
        List<string> unsupportedFields,
        HashSet<string> unsupportedSeen)
    {
        var mapped = new Dictionary<DiskExplorerField, string>();
        var extra = new Dictionary<string, string>(StringComparer.Ordinal);
        string? volume = null;
        string? category = null;
        string? path = null;
        string? fullPath = null;
        string? rawKind = null;

        for (var i = 0; i < header.Columns.Count; i++)
        {
            var column = header.Columns[i];
            var value = fields[i];
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            if (column.Structural)
            {
                if (column.Label.Equals("VOLUME", StringComparison.OrdinalIgnoreCase)) volume = value.Trim();
                else if (column.Label.Equals("CATEGORY", StringComparison.OrdinalIgnoreCase)) category = value.Trim();
                else if (column.Label.Equals("PATH", StringComparison.OrdinalIgnoreCase)) path = NormalizePath(value);
                else if (column.Label.Equals("FULLPATH", StringComparison.OrdinalIgnoreCase)) fullPath = NormalizePath(value);
                else if (column.Label.Equals("ITEMTYPE", StringComparison.OrdinalIgnoreCase)
                    || column.Label.Equals("KIND", StringComparison.OrdinalIgnoreCase)) rawKind = value.Trim();
                continue;
            }

            if (column.Field != DiskExplorerField.Unknown)
            {
                mapped[column.Field] = value.Trim();
                continue;
            }

            var key = "DiskExplorer." + column.Label.Trim().ToUpperInvariant();
            extra.TryAdd(key, value);
            if (unsupportedSeen.Add(key))
            {
                unsupportedFields.Add(key);
            }
        }

        var kind = DiskExplorerElementKind.Unknown;
        if (!string.IsNullOrWhiteSpace(rawKind) && DiskExplorerNames.TryParseItemType(rawKind, out var parsed))
        {
            kind = parsed;
        }
        else if (string.IsNullOrWhiteSpace(path) && string.IsNullOrWhiteSpace(fullPath)
            && !mapped.ContainsKey(DiskExplorerField.Name) && !string.IsNullOrWhiteSpace(volume))
        {
            kind = DiskExplorerElementKind.Volume;
        }
        else if (mapped.ContainsKey(DiskExplorerField.Files) || mapped.ContainsKey(DiskExplorerField.Folders))
        {
            kind = DiskExplorerElementKind.Folder;
        }
        else
        {
            kind = DiskExplorerElementKind.File;
        }

        string? relativePath = path ?? DeriveRelativePath(fullPath, volume);
        if (!string.IsNullOrWhiteSpace(category))
        {
            var clean = category!.Trim().Replace('\\', '/').Trim('/');
            relativePath = string.IsNullOrWhiteSpace(relativePath) ? clean : relativePath + "/" + clean;
        }

        if (kind == DiskExplorerElementKind.Volume)
        {
            relativePath = null;
            if (!mapped.ContainsKey(DiskExplorerField.Name) && !string.IsNullOrWhiteSpace(volume))
            {
                mapped[DiskExplorerField.Name] = volume!;
            }
        }
        else if (kind is DiskExplorerElementKind.Folder or DiskExplorerElementKind.CategoryFolder
            && !string.IsNullOrWhiteSpace(relativePath))
        {
            // Folder rows may carry the parent path with the folder name in NAME.
            var folderName = mapped.TryGetValue(DiskExplorerField.Name, out var explicitName)
                && !string.IsNullOrWhiteSpace(explicitName)
                ? explicitName.Trim()
                : relativePath![(relativePath!.LastIndexOf('/') + 1)..];
            if (path is not null && !EndsWithSegment(relativePath!, folderName))
            {
                relativePath = path + "/" + folderName;
            }

            mapped[DiskExplorerField.Name] = folderName;
        }
        else if (!mapped.ContainsKey(DiskExplorerField.Name) && !string.IsNullOrWhiteSpace(relativePath))
        {
            var slash = relativePath!.LastIndexOf('/');
            mapped[DiskExplorerField.Name] = slash < 0 ? relativePath : relativePath[(slash + 1)..];
        }

        return new RowRecord(kind, rawKind, volume, relativePath, mapped, extra, ParentPosition: -1);
    }

    private static bool EndsWithSegment(string path, string name)
    {
        var slash = path.LastIndexOf('/');
        var last = slash < 0 ? path : path[(slash + 1)..];
        return last.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizePath(string value)
    {
        var normalized = value.Replace('\\', '/').Trim().Trim('/');
        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        return normalized.Length == 0 ? null : normalized;
    }

    private static string? DeriveRelativePath(string? fullPath, string? volume)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return null;
        }

        var normalized = fullPath;
        if (!string.IsNullOrWhiteSpace(volume))
        {
            var prefix = volume!.Replace('\\', '/').Trim('/');
            if (normalized.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[prefix.Length..].Trim('/');
            }
            else if (normalized.Equals(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return normalized.Length == 0 ? null : normalized;
    }

    private static int SynthesizeMissingHierarchy(List<RowRecord> records)
    {
        var volumes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folders = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            if (record.Kind == DiskExplorerElementKind.Volume && !string.IsNullOrWhiteSpace(record.Volume))
            {
                volumes.Add(record.Volume!);
            }

            if (record.Kind is DiskExplorerElementKind.Folder or DiskExplorerElementKind.CategoryFolder
                && record.Volume is not null && record.RelativePath is not null)
            {
                folders.Add($"{record.Volume}\0{record.RelativePath}");
            }
        }

        var synthetics = new List<RowRecord>();
        foreach (var record in records)
        {
            if (string.IsNullOrWhiteSpace(record.Volume) || record.Kind == DiskExplorerElementKind.Volume)
            {
                continue;
            }

            if (!volumes.Add(record.Volume!))
            {
                AddAncestorFolders(synthetics, folders, record);
                continue;
            }

            synthetics.Add(new RowRecord(
                DiskExplorerElementKind.Volume,
                RawKind: null,
                Volume: record.Volume,
                RelativePath: null,
                Fields: new Dictionary<DiskExplorerField, string>
                {
                    [DiskExplorerField.Name] = record.Volume!
                },
                Extra: new Dictionary<string, string>(StringComparer.Ordinal),
                ParentPosition: -1));
            AddAncestorFolders(synthetics, folders, record);
        }

        records.InsertRange(0, synthetics);
        return synthetics.Count;
    }

    private static void AddAncestorFolders(
        List<RowRecord> synthetics, HashSet<string> folders, RowRecord record)
    {
        if (record.Volume is null || record.RelativePath is null)
        {
            return;
        }

        var segments = record.RelativePath.Split('/');
        var folderDepth = record.Kind is DiskExplorerElementKind.Folder or DiskExplorerElementKind.CategoryFolder
            ? segments.Length - 1
            : segments.Length;
        var prefix = new System.Text.StringBuilder();
        for (var i = 0; i < folderDepth; i++)
        {
            if (prefix.Length > 0)
            {
                prefix.Append('/');
            }

            prefix.Append(segments[i]);
            var ancestor = prefix.ToString();
            if (!folders.Add($"{record.Volume}\0{ancestor}"))
            {
                continue;
            }

            synthetics.Add(new RowRecord(
                DiskExplorerElementKind.Folder,
                RawKind: null,
                Volume: record.Volume,
                RelativePath: ancestor,
                Fields: new Dictionary<DiskExplorerField, string>
                {
                    [DiskExplorerField.Name] = segments[i]
                },
                Extra: new Dictionary<string, string>(StringComparer.Ordinal),
                ParentPosition: -1));
        }
    }

    private static bool ResolveParents(List<RowRecord> records, int syntheticCount)
    {
        var volumes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var folders = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < records.Count; i++)
        {
            var record = records[i];
            if (record.Kind == DiskExplorerElementKind.Volume && !string.IsNullOrWhiteSpace(record.Volume))
            {
                volumes.TryAdd(record.Volume!, i);
            }

            if (record.Kind is DiskExplorerElementKind.Folder or DiskExplorerElementKind.CategoryFolder
                && record.Volume is not null && record.RelativePath is not null)
            {
                folders.TryAdd($"{record.Volume}\0{record.RelativePath}", i);
            }
        }

        var partial = false;
        for (var i = 0; i < records.Count; i++)
        {
            var record = records[i];
            if (record.Kind == DiskExplorerElementKind.Volume)
            {
                records[i] = record with { ParentPosition = -1 };
                continue;
            }

            var parent = -1;
            if (record.Volume is not null && record.RelativePath is not null)
            {
                // Files live inside their relative path; folders live at it.
                string parentPath;
                if (record.Kind is DiskExplorerElementKind.Folder or DiskExplorerElementKind.CategoryFolder)
                {
                    var slash = record.RelativePath.LastIndexOf('/');
                    parentPath = slash < 0 ? string.Empty : record.RelativePath[..slash];
                }
                else
                {
                    parentPath = record.RelativePath;
                }

                if (parentPath.Length == 0)
                {
                    if (!volumes.TryGetValue(record.Volume, out parent))
                    {
                        parent = -1;
                    }
                }
                else if (!folders.TryGetValue($"{record.Volume}\0{parentPath}", out parent))
                {
                    if (!volumes.TryGetValue(record.Volume, out parent))
                    {
                        parent = -1;
                    }
                }
            }
            else if (record.Volume is not null && record.Volume.Length > 0
                && !volumes.TryGetValue(record.Volume, out parent))
            {
                parent = -1;
            }
            else if (record.Volume is null || record.Volume.Length == 0)
            {
                parent = -1;
            }

            if (parent < 0 || parent < syntheticCount)
            {
                partial = true;
            }

            records[i] = record with { ParentPosition = parent };
        }

        TopoSort(records);
        return partial;
    }

    private static void TopoSort(List<RowRecord> records)
    {
        var emitted = new bool[records.Count];
        var order = new List<int>(records.Count);
        var remaining = records.Count;
        while (remaining > 0)
        {
            var progressed = false;
            for (var i = 0; i < records.Count; i++)
            {
                if (emitted[i])
                {
                    continue;
                }

                var parent = records[i].ParentPosition;
                if (parent < 0 || emitted[parent])
                {
                    emitted[i] = true;
                    order.Add(i);
                    remaining--;
                    progressed = true;
                }
            }

            if (!progressed)
            {
                break;
            }
        }

        if (order.Count != records.Count)
        {
            return;
        }

        var position = new int[records.Count];
        for (var i = 0; i < order.Count; i++)
        {
            position[order[i]] = i;
        }

        var sorted = new List<RowRecord>(records.Count);
        foreach (var old in order)
        {
            var record = records[old];
            sorted.Add(record.ParentPosition < 0
                ? record with { ParentPosition = -1 }
                : record with { ParentPosition = position[record.ParentPosition] });
        }

        records.Clear();
        records.AddRange(sorted);
    }

    private static List<DiskExplorerItem?> BuildItems(List<RowRecord> records)
    {
        var items = new List<DiskExplorerItem?>(records.Count);
        for (var i = 0; i < records.Count; i++)
        {
            var record = records[i];
            items.Add(new DiskExplorerItem(
                Index: i,
                Kind: record.Kind,
                RawItemType: record.RawKind,
                ParentIndex: record.ParentPosition,
                HasItemChildren: false,
                HasThumbnail: false,
                Fields: record.Fields,
                ExtraFields: record.Extra));
        }

        return items;
    }
}
