using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SkyCD.Plugin.NeoFinder.Text.Exceptions;

namespace SkyCD.Plugin.NeoFinder.Text;

/// <summary>
/// Reconstructs hierarchical catalog entries and metadata from NeoFinder text export rows.
/// </summary>
/// <remarks>
/// The exporter merges every selected catalog into one file, so rows carrying different
/// volume or catalog values are grouped under their own media root. The parser never touches
/// the filesystem: thumbnail references or external paths in the export are treated as plain
/// text and are never loaded.
/// </remarks>
public static class NeoFinderTextHierarchyBuilder
{
    private const int CancelCheckEveryRows = 500;

    public static NeoFinderTextReportDocument Build(
        List<List<string>> rawRows,
        char delimiter,
        NeoFinderTextEncodingProfile? encodingProfile = null,
        string? fileStem = null,
        CancellationToken cancellationToken = default)
    {
        if (rawRows.Count == 0)
        {
            throw new NeoFinderTextCorruptHeaderException();
        }

        var headerRow = rawRows[0];
        if (headerRow.Count == 0 || headerRow.All(string.IsNullOrWhiteSpace))
        {
            throw new NeoFinderTextCorruptHeaderException();
        }

        var columnMap = MapHeader(headerRow);

        if (!columnMap.ContainsKey(NeoFinderTextColumn.Name))
        {
            throw new NeoFinderTextAmbiguousLayoutException(
                "No Name column was recognised. Headers are required.");
        }

        var hasLocation = columnMap.ContainsKey(NeoFinderTextColumn.Path)
            || columnMap.ContainsKey(NeoFinderTextColumn.Volume)
            || columnMap.ContainsKey(NeoFinderTextColumn.Catalog);

        var warnings = new List<string>();
        if (!columnMap.ContainsKey(NeoFinderTextColumn.Path))
        {
            warnings.Add(columnMap.ContainsKey(NeoFinderTextColumn.Volume) || columnMap.ContainsKey(NeoFinderTextColumn.Catalog)
                ? "Warning: no path column was recognised; entries are imported flat under their media root."
                : "Warning: no path, volume or catalog column was recognised; entries are imported flat under a generic media root.");
        }

        var unsupportedFields = headerRow
            .Where(h => !string.IsNullOrWhiteSpace(h) && NeoFinderTextProfile.MapColumn(h) == NeoFinderTextColumn.Unknown)
            .Select(h => h.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totalRecords = rawRows.Count - 1;
        var skippedReasons = new List<string>(warnings);
        var skippedRecordsCount = 0;
        var importedRecordsCount = 0;

        var nodesById = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        var synthesizedFolderPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mediaRoots = new HashSet<string>(StringComparer.Ordinal);

        for (var rowIndex = 1; rowIndex < rawRows.Count; rowIndex++)
        {
            if (rowIndex % CancelCheckEveryRows == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var row = rawRows[rowIndex];
            if (row.Count == 0 || (row.Count == 1 && string.IsNullOrWhiteSpace(row[0])))
            {
                continue;
            }

            try
            {
                ProcessRow(row, columnMap, fileStem, nodesById, synthesizedFolderPaths, mediaRoots);
                importedRecordsCount++;
            }
            catch (Exception ex)
            {
                skippedRecordsCount++;
                skippedReasons.Add($"Row {rowIndex + 1}: {ex.Message}");
            }
        }

        if (importedRecordsCount == 0)
        {
            throw new NeoFinderTextEmptyExportException();
        }

        var isPartialReport = synthesizedFolderPaths.Count > 0 || skippedRecordsCount > 0 || warnings.Count > 0;

        var summary = new NeoFinderTextImportSummary(
            TotalRecordsRead: totalRecords,
            ImportedRecordsCount: importedRecordsCount,
            SkippedRecordsCount: skippedRecordsCount,
            IsPartialReport: isPartialReport,
            SkippedReasons: skippedReasons,
            UnsupportedFields: unsupportedFields,
            DetectedDelimiter: delimiter,
            DetectedColumns: headerRow,
            DetectedEncoding: encodingProfile?.DisplayName ?? NeoFinderTextEncodingProfile.Unknown.DisplayName);

        return new NeoFinderTextReportDocument(nodesById.Values.ToList(), summary);
    }

    private static Dictionary<NeoFinderTextColumn, int> MapHeader(List<string> headerRow)
    {
        var columnMap = new Dictionary<NeoFinderTextColumn, int>();
        foreach (var (header, index) in headerRow.Select((h, i) => (h, i)))
        {
            if (string.IsNullOrWhiteSpace(header))
            {
                continue;
            }

            var column = NeoFinderTextProfile.MapColumn(header);
            if (column == NeoFinderTextColumn.Unknown)
            {
                continue;
            }

            if (columnMap.ContainsKey(column))
            {
                throw new NeoFinderTextAmbiguousLayoutException(
                    $"Multiple headers map to '{column}'.");
            }

            columnMap[column] = index;
        }

        return columnMap;
    }

    private static void ProcessRow(
        List<string> row,
        Dictionary<NeoFinderTextColumn, int> columnMap,
        string? fileStem,
        Dictionary<string, Dictionary<string, object?>> nodesById,
        HashSet<string> synthesizedFolderPaths,
        HashSet<string> mediaRoots)
    {
        var name = Normalize(GetValue(row, columnMap, NeoFinderTextColumn.Name));
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Row has no usable name.");
        }

        var kindText = GetValue(row, columnMap, NeoFinderTextColumn.Kind);
        var pathText = GetValue(row, columnMap, NeoFinderTextColumn.Path);
        var volume = GetValue(row, columnMap, NeoFinderTextColumn.Volume);
        var catalog = GetValue(row, columnMap, NeoFinderTextColumn.Catalog);
        var isExplicitFolder = IsFolderKind(kindText);

        var mediaName = !string.IsNullOrWhiteSpace(volume)
            ? volume.Trim()
            : !string.IsNullOrWhiteSpace(catalog)
                ? catalog.Trim()
                : !string.IsNullOrWhiteSpace(fileStem) ? fileStem.Trim() : "NeoFinder";
        mediaName = Normalize(mediaName);

        List<string> folderSegments;
        string leafName;
        if (!string.IsNullOrWhiteSpace(pathText))
        {
            (folderSegments, leafName, isExplicitFolder) = SplitExportPath(pathText, name, isExplicitFolder);

            // Exports often repeat the volume or catalog as the first path segment; that segment
            // names the media root itself rather than a subfolder.
            if (folderSegments.Count > 0 &&
                ((!string.IsNullOrWhiteSpace(volume) && folderSegments[0].Equals(volume.Trim(), StringComparison.OrdinalIgnoreCase)) ||
                 (!string.IsNullOrWhiteSpace(catalog) && folderSegments[0].Equals(catalog.Trim(), StringComparison.OrdinalIgnoreCase))))
            {
                folderSegments.RemoveAt(0);
            }
        }
        else
        {
            folderSegments = [];
            leafName = name;
        }

        var mediaId = $"media:{mediaName}";
        if (mediaRoots.Add(mediaId))
        {
            nodesById[mediaId] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["nodeId"] = mediaId,
                ["parentId"] = null,
                ["kind"] = "media",
                ["name"] = mediaName,
                ["sizeBytes"] = "0",
                ["properties"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["NeoFinderSource"] = "text-export",
                }
            };
        }

        var currentParentId = mediaId;
        var currentPath = mediaName;
        foreach (var folder in folderSegments)
        {
            currentPath = $"{currentPath}/{folder}";
            var folderId = $"folder:{currentPath}";
            if (!nodesById.ContainsKey(folderId))
            {
                synthesizedFolderPaths.Add(currentPath);
                nodesById[folderId] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["nodeId"] = folderId,
                    ["parentId"] = currentParentId,
                    ["kind"] = "folder",
                    ["name"] = folder,
                    ["sizeBytes"] = "0",
                    ["properties"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                };
            }

            currentParentId = folderId;
        }

        var sizeText = GetValue(row, columnMap, NeoFinderTextColumn.Size);
        var createdText = GetValue(row, columnMap, NeoFinderTextColumn.Created);
        var modifiedText = GetValue(row, columnMap, NeoFinderTextColumn.Modified);
        var comments = GetValue(row, columnMap, NeoFinderTextColumn.Comments);
        var label = GetValue(row, columnMap, NeoFinderTextColumn.Label);

        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(pathText)) properties["NeoFinderPath"] = pathText.Trim();
        if (!string.IsNullOrWhiteSpace(volume)) properties["NeoFinderVolume"] = volume.Trim();
        if (!string.IsNullOrWhiteSpace(catalog)) properties["NeoFinderCatalog"] = catalog.Trim();
        if (!string.IsNullOrWhiteSpace(kindText)) properties["NeoFinderKind"] = kindText.Trim();
        if (!string.IsNullOrWhiteSpace(createdText)) properties["DateCreated"] = NormalizeDate(createdText);
        if (!string.IsNullOrWhiteSpace(modifiedText)) properties["DateModified"] = NormalizeDate(modifiedText);
        if (!string.IsNullOrWhiteSpace(comments)) properties["Comments"] = comments.Trim();
        if (!string.IsNullOrWhiteSpace(label)) properties["Label"] = label.Trim();

        var kind = isExplicitFolder ? "folder" : "file";
        if (isExplicitFolder)
        {
            synthesizedFolderPaths.Remove($"{currentPath}/{leafName}");
        }

        var nodeId = $"{kind}:{currentPath}/{leafName}";
        if (nodesById.ContainsKey(nodeId))
        {
            nodeId = $"{nodeId}#{nodesById.Count}";
        }

        nodesById[nodeId] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["nodeId"] = nodeId,
            ["parentId"] = currentParentId,
            ["kind"] = kind,
            ["name"] = leafName,
            ["sizeBytes"] = ParseSize(sizeText).ToString(CultureInfo.InvariantCulture),
            ["properties"] = properties
        };
    }

    /// <summary>
    /// Splits an export path into folder segments and a leaf name.
    /// </summary>
    /// <remarks>
    /// NeoFinder exports macOS paths, so both POSIX (<c>/</c>) and classic Mac OS
    /// (<c>:</c>) separators are recognised. A trailing separator marks an explicit
    /// folder even when no kind column is present.
    /// </remarks>
    private static (List<string> FolderSegments, string LeafName, bool IsFolder) SplitExportPath(
        string pathText, string rowName, bool isExplicitFolder)
    {
        var normalized = Normalize(pathText.Trim());
        var trailingFolder = normalized.EndsWith(':') || normalized.EndsWith('/') || normalized.EndsWith('\\');
        var unified = normalized.Replace('\\', '/').Replace(':', '/');
        var parts = unified.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(p => p.Length > 0)
            .ToList();

        if (parts.Count == 0)
        {
            return ([], Normalize(rowName), isExplicitFolder);
        }

        // When the final segment matches the row name the path addresses the item itself;
        // otherwise the path addresses the containing folder.
        if (parts[^1].Equals(Normalize(rowName), StringComparison.OrdinalIgnoreCase))
        {
            var leaf = parts[^1];
            parts.RemoveAt(parts.Count - 1);
            return (parts, leaf, isExplicitFolder || trailingFolder);
        }

        return (parts, Normalize(rowName), isExplicitFolder || trailingFolder);
    }

    private static bool IsFolderKind(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return false;
        return kind.Trim().ToLowerInvariant() switch
        {
            "folder" or "file folder" or "directory" or "volume" or "disk" or "laufwerk" or "datenträger" or "ordner"
                or "dossier" or "répertoire" or "carpeta" or "directorio" or "cartella"
                or "папка" => true,
            _ => false
        };
    }

    /// <summary>
    /// Parses a NeoFinder size cell into bytes.
    /// </summary>
    /// <remarks>
    /// Handles raw byte counts, unit suffixes (<c>B</c>/<c>KB</c>/<c>MB</c>/<c>GB</c>/<c>TB</c>
    /// including localized variants), European comma decimals and thousands separators.
    /// Unparseable values map to zero rather than failing the row.
    /// </remarks>
    public static long ParseSize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return 0L;
        }

        var trimmed = raw.Trim();
        if (trimmed is "—" or "-" or "--")
        {
            return 0L;
        }

        var unitMatch = Regex.Match(trimmed, @"^([0-9.,\s]+)\s*([a-zA-Zµ]+)$");
        if (unitMatch.Success)
        {
            var numberPart = unitMatch.Groups[1].Value.Trim().Replace(" ", string.Empty);
            var unit = unitMatch.Groups[2].Value.Trim().ToUpperInvariant();

            // European comma decimal separator e.g. "12,5 MB"
            if (numberPart.Contains(',') && !numberPart.Contains('.'))
            {
                numberPart = numberPart.Replace(',', '.');
            }
            else if (numberPart.Contains(',') && numberPart.Contains('.'))
            {
                // Grouping separators e.g. "1,048.5" or "1.048,5"
                if (numberPart.LastIndexOf('.') > numberPart.LastIndexOf(','))
                {
                    numberPart = numberPart.Replace(",", string.Empty);
                }
                else
                {
                    numberPart = numberPart.Replace(".", string.Empty).Replace(',', '.');
                }
            }

            if (double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedValue))
            {
                var multiplier = unit switch
                {
                    "TB" or "T" or "TO" => 1024L * 1024 * 1024 * 1024,
                    "GB" or "G" or "GO" => 1024L * 1024 * 1024,
                    "MB" or "M" or "MO" or "MIO" => 1024L * 1024,
                    "KB" or "K" or "KO" or "KIO" => 1024L,
                    "B" or "BYTE" or "BYTES" or "O" or "OCTET" or "OCTETS" => 1L,
                    _ => 1L
                };

                return (long)Math.Round(parsedValue * multiplier);
            }
        }

        // Clean integer formats: remove spaces, dots or commas used as thousand separators
        var digitsOnly = Regex.Replace(trimmed, @"[^\d]", string.Empty);
        return long.TryParse(digitsOnly, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) ? size : 0L;
    }

    private static readonly CultureInfo[] SlashFirstCultures =
    [
        CultureInfo.InvariantCulture,
        new CultureInfo("en-US"),
        new CultureInfo("de-DE"),
        new CultureInfo("fr-FR"),
        new CultureInfo("es-ES"),
        new CultureInfo("it-IT"),
        new CultureInfo("ru-RU"),
    ];

    private static readonly CultureInfo[] DotFirstCultures =
    [
        new CultureInfo("de-DE"),
        new CultureInfo("fr-FR"),
        new CultureInfo("es-ES"),
        new CultureInfo("it-IT"),
        new CultureInfo("ru-RU"),
        CultureInfo.InvariantCulture,
        new CultureInfo("en-US"),
    ];

    /// <summary>
    /// Normalizes a NeoFinder date cell to <c>yyyy-MM-dd HH:mm:ss</c>.
    /// </summary>
    /// <remarks>
    /// The export dialog offers locale-dependent date formats, so several cultures are tried in order.
    /// Dotted numeric dates (<c>06.01.2026</c>) are day-first in every exporter locale that uses dots,
    /// while invariant parsing would misread them as month-first, so day-first cultures go first for
    /// dotted inputs. Slashed numeric dates stay month-first (US/ISO convention); a day-first slashed
    /// date with day and month both under 13 cannot be distinguished and keeps that reading.
    /// Values matching no culture are preserved verbatim rather than dropping the row.
    /// </remarks>
    public static string NormalizeDate(string raw)
    {
        var trimmed = raw.Trim();
        var cultures = trimmed.Contains('.') && !trimmed.Contains('/')
            ? DotFirstCultures
            : SlashFirstCultures;
        foreach (var culture in cultures)
        {
            if (DateTimeOffset.TryParse(trimmed, culture, DateTimeStyles.None, out var dto))
            {
                return dto.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
        }

        return trimmed;
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Normalize(NormalizationForm.FormC);

    private static string? GetValue(List<string> row, Dictionary<NeoFinderTextColumn, int> map, NeoFinderTextColumn col)
    {
        if (!map.TryGetValue(col, out var index) || index < 0 || index >= row.Count)
        {
            return null;
        }

        var value = row[index];
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
