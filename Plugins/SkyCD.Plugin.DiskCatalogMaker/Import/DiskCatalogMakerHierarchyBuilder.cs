using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SkyCD.Plugin.DiskCatalogMaker.Exceptions;
using SkyCD.Plugin.DiskCatalogMaker.Models;

namespace SkyCD.Plugin.DiskCatalogMaker.Import;

/// <summary>
/// Reconstructs hierarchical catalog entries and metadata from DiskCatalogMaker text-export rows.
/// </summary>
public static class DiskCatalogMakerHierarchyBuilder
{
    public static DiskCatalogMakerReportDocument Build(
        List<List<string>> rawRows,
        char delimiter,
        DiskCatalogMakerEncodingProfile? encodingProfile = null)
    {
        if (rawRows.Count == 0)
        {
            throw new DiskCatalogMakerCorruptHeaderException();
        }

        var headerRow = rawRows[0];
        if (headerRow.Count == 0 || headerRow.All(string.IsNullOrWhiteSpace))
        {
            throw new DiskCatalogMakerCorruptHeaderException();
        }

        // Map column indices
        var columnMap = new Dictionary<DiskCatalogMakerColumn, int>();
        var unsupportedFields = new List<string>();

        for (var i = 0; i < headerRow.Count; i++)
        {
            var header = headerRow[i];
            var col = DiskCatalogMakerProfile.MapColumn(header);
            if (col != DiskCatalogMakerColumn.Unknown)
            {
                columnMap.TryAdd(col, i);
            }
            else if (!string.IsNullOrWhiteSpace(header))
            {
                unsupportedFields.Add(header.Trim());
            }
        }

        // The Path column is mandatory: without it no hierarchy can be recovered.
        if (!columnMap.ContainsKey(DiskCatalogMakerColumn.Path))
        {
            throw new DiskCatalogMakerMissingPathColumnException();
        }

        var totalRecords = rawRows.Count - 1;
        if (totalRecords == 0)
        {
            throw new DiskCatalogMakerEmptyReportException();
        }

        var skippedReasons = new List<string>();
        var skippedRecordsCount = 0;
        var importedRecordsCount = 0;

        var nodesById = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        var synthesizedFolderPaths = new HashSet<string>(StringComparer.Ordinal);

        for (var rowIndex = 1; rowIndex < rawRows.Count; rowIndex++)
        {
            var row = rawRows[rowIndex];
            if (row.Count == 0 || (row.Count == 1 && string.IsNullOrWhiteSpace(row[0])))
            {
                continue;
            }

            try
            {
                ProcessRow(
                    row,
                    rowIndex,
                    columnMap,
                    nodesById,
                    synthesizedFolderPaths);

                importedRecordsCount++;
            }
            catch (Exception ex)
            {
                skippedRecordsCount++;
                skippedReasons.Add($"Row {rowIndex + 1}: {ex.Message}");
            }
        }

        // A report is partial if intermediate folders had to be synthesized or if any rows were skipped
        var isPartialReport = synthesizedFolderPaths.Count > 0 || skippedRecordsCount > 0;

        var summary = new DiskCatalogMakerImportSummary(
            TotalRecordsRead: totalRecords,
            ImportedRecordsCount: importedRecordsCount,
            SkippedRecordsCount: skippedRecordsCount,
            IsPartialReport: isPartialReport,
            SkippedReasons: skippedReasons,
            UnsupportedFields: unsupportedFields,
            DetectedDelimiter: delimiter,
            DetectedColumns: headerRow,
            DetectedEncoding: encodingProfile?.DisplayName ?? DiskCatalogMakerEncodingProfile.Unknown.DisplayName);

        var rowsList = nodesById.Values.ToList();
        return new DiskCatalogMakerReportDocument(rowsList, summary);
    }

    private static void ProcessRow(
        List<string> row,
        int rowIndex,
        Dictionary<DiskCatalogMakerColumn, int> columnMap,
        Dictionary<string, Dictionary<string, object?>> nodesById,
        HashSet<string> synthesizedFolderPaths)
    {
        var path = GetValue(row, columnMap, DiskCatalogMakerColumn.Path);
        var name = GetValue(row, columnMap, DiskCatalogMakerColumn.Name);
        var sizeText = GetValue(row, columnMap, DiskCatalogMakerColumn.Size);
        var kindText = GetValue(row, columnMap, DiskCatalogMakerColumn.Kind);
        var modifiedText = GetValue(row, columnMap, DiskCatalogMakerColumn.DateModified);
        var createdText = GetValue(row, columnMap, DiskCatalogMakerColumn.DateCreated);
        var creator = GetValue(row, columnMap, DiskCatalogMakerColumn.Creator);
        var typeCode = GetValue(row, columnMap, DiskCatalogMakerColumn.TypeCode);
        var comments = GetValue(row, columnMap, DiskCatalogMakerColumn.Comments);
        var label = GetValue(row, columnMap, DiskCatalogMakerColumn.Label);
        var disk = GetValue(row, columnMap, DiskCatalogMakerColumn.Disk);

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Row does not specify a Path.");
        }

        var trimmedPath = path.Trim();
        var isHfsPath = !trimmedPath.Contains('/') && trimmedPath.Contains(':');
        var isExplicitFolder = IsFolderKind(kindText) || trimmedPath.EndsWith('/') ||
            (isHfsPath && trimmedPath.EndsWith(':'));

        // Determine media name and relative folder segments from the macOS path
        var segments = SplitMacPath(trimmedPath);
        if (segments.Count == 0)
        {
            throw new InvalidOperationException("Row Path contains no usable segments.");
        }

        string mediaName;
        if (!string.IsNullOrWhiteSpace(disk))
        {
            mediaName = disk.Trim();
            if (trimmedPath.StartsWith('/') || (isHfsPath && !trimmedPath.StartsWith(':') &&
                    segments[0].Equals(mediaName, StringComparison.Ordinal)))
            {
                segments.RemoveAt(0);
            }
        }
        else
        {
            mediaName = segments[0];
            segments.RemoveAt(0);
        }

        var preferredName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        var isMediaRoot = segments.Count == 0 &&
            (preferredName is null || preferredName.Equals(mediaName, StringComparison.Ordinal));
        string leafName;
        List<string> folderSegments;

        if (isMediaRoot)
        {
            leafName = mediaName;
            folderSegments = [];
        }
        else if (isExplicitFolder)
        {
            if (!string.IsNullOrWhiteSpace(preferredName) &&
                (segments.Count == 0 || !segments[^1].Equals(preferredName, StringComparison.Ordinal)))
            {
                segments.Add(preferredName);
            }

            folderSegments = segments;
            leafName = folderSegments.Count > 0 ? folderSegments[^1] : (preferredName ?? mediaName);
            if (folderSegments.Count > 0)
            {
                folderSegments.RemoveAt(folderSegments.Count - 1);
            }
        }
        else
        {
            if (segments.Count > 0 && !string.IsNullOrWhiteSpace(preferredName) &&
                segments[^1].Equals(preferredName, StringComparison.Ordinal))
            {
                leafName = segments[^1];
                segments.RemoveAt(segments.Count - 1);
                folderSegments = segments;
            }
            else if (!string.IsNullOrWhiteSpace(preferredName))
            {
                leafName = preferredName;
                folderSegments = segments;
            }
            else if (segments.Count > 0)
            {
                leafName = segments[^1];
                segments.RemoveAt(segments.Count - 1);
                folderSegments = segments;
            }
            else
            {
                leafName = $"Item_{rowIndex}";
                folderSegments = [];
            }
        }

        // 1. Ensure media root exists
        var mediaId = EncodeNodeId($"media:{mediaName}");
        if (!nodesById.ContainsKey(mediaId))
        {
            nodesById[mediaId] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["nodeId"] = mediaId,
                ["parentId"] = null,
                ["kind"] = "media",
                ["name"] = mediaName,
                ["sizeBytes"] = "0",
                ["properties"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            };
        }

        if (isMediaRoot)
        {
            nodesById[mediaId]["sizeBytes"] = ParseSize(sizeText).ToString(CultureInfo.InvariantCulture);
            nodesById[mediaId]["properties"] = BuildProperties(path, createdText, modifiedText, kindText, creator, typeCode, comments, label, disk);
            return;
        }

        // 2. Ensure intermediate folders exist
        var currentParentId = mediaId;
        var currentPath = mediaName;
        for (var i = 0; i < folderSegments.Count; i++)
        {
            var folderName = folderSegments[i];
            currentPath = $"{currentPath}/{folderName}";
            var folderId = EncodeNodeId($"folder:{currentPath}");

            if (!nodesById.ContainsKey(folderId))
            {
                synthesizedFolderPaths.Add(currentPath);
                nodesById[folderId] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["nodeId"] = folderId,
                    ["parentId"] = currentParentId,
                    ["kind"] = "folder",
                    ["name"] = folderName,
                    ["sizeBytes"] = "0",
                    ["properties"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                };
            }

            currentParentId = folderId;
        }

        // 3. Create or update the target node
        if (isExplicitFolder)
        {
            var folderPath = $"{currentPath}/{leafName}";
            var folderId = EncodeNodeId($"folder:{folderPath}");
            synthesizedFolderPaths.Remove(folderPath);

            var folderProperties = BuildProperties(path, createdText, modifiedText, kindText, creator, typeCode, comments, label, disk);

            nodesById[folderId] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["nodeId"] = folderId,
                ["parentId"] = currentParentId,
                ["kind"] = "folder",
                ["name"] = leafName,
                ["sizeBytes"] = ParseSize(sizeText).ToString(CultureInfo.InvariantCulture),
                ["properties"] = folderProperties
            };
        }
        else
        {
            var fileId = EncodeNodeId($"file:{currentPath}/{leafName}");
            var fileProperties = BuildProperties(path, createdText, modifiedText, kindText, creator, typeCode, comments, label, disk);

            nodesById[fileId] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["nodeId"] = fileId,
                ["parentId"] = currentParentId,
                ["kind"] = "file",
                ["name"] = leafName,
                ["sizeBytes"] = ParseSize(sizeText).ToString(CultureInfo.InvariantCulture),
                ["properties"] = fileProperties
            };
        }
    }

    private static string EncodeNodeId(string value)
    {
        var encoded = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is '%' or <= '\u001F' or '\u007F')
            {
                encoded.Append('%').Append(((int)character).ToString("X2", CultureInfo.InvariantCulture));
            }
            else
            {
                encoded.Append(character);
            }
        }

        return encoded.ToString();
    }

    private static Dictionary<string, object?> BuildProperties(
        string? path,
        string? createdText,
        string? modifiedText,
        string? kindText,
        string? creator,
        string? typeCode,
        string? comments,
        string? label,
        string? disk)
    {
        var props = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(path)) props["DiskCatalogMakerPath"] = path;
        if (!string.IsNullOrWhiteSpace(kindText)) props["Kind"] = kindText;
        if (!string.IsNullOrWhiteSpace(creator)) props["Creator"] = creator;
        if (!string.IsNullOrWhiteSpace(typeCode)) props["TypeCode"] = typeCode;
        if (!string.IsNullOrWhiteSpace(comments)) props["Comments"] = comments;
        if (!string.IsNullOrWhiteSpace(label)) props["Label"] = label;
        if (!string.IsNullOrWhiteSpace(disk)) props["Volume"] = disk;
        if (!string.IsNullOrWhiteSpace(createdText)) props["DateCreated"] = NormalizeDate(createdText);
        if (!string.IsNullOrWhiteSpace(modifiedText)) props["DateModified"] = NormalizeDate(modifiedText);

        return props;
    }

    /// <summary>
    /// Splits a macOS export path into segments. POSIX slash-separated paths are primary;
    /// classic HFS colon-separated paths are supported when no slash is present.
    /// A leading "/Volumes/" mount prefix is stripped so the volume name becomes the media root.
    /// Backslashes are preserved: they are valid macOS filename characters, not separators.
    /// </summary>
    public static List<string> SplitMacPath(string path)
    {
        var trimmed = path.Trim().TrimEnd('\0');

        List<string> parts;
        if (trimmed.Contains('/'))
        {
            parts = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
        else if (trimmed.Contains(':'))
        {
            parts = trimmed.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
        else
        {
            parts = string.IsNullOrWhiteSpace(trimmed) ? [] : [trimmed];
        }

        if (parts.Count >= 2 && trimmed.StartsWith("/Volumes/", StringComparison.Ordinal))
        {
            parts.RemoveAt(0);
        }

        return parts;
    }

    public static long ParseSize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return 0L;
        }

        var trimmed = raw.Trim();

        // Check unit suffix
        var unitMatch = Regex.Match(trimmed, @"^([0-9.,\s]+)\s*([a-zA-Z]+)$");
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
                // Grouping separators e.g. "1,048.5"
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
                    "TB" or "T" => 1024L * 1024 * 1024 * 1024,
                    "GB" or "G" => 1024L * 1024 * 1024,
                    "MB" or "M" => 1024L * 1024,
                    "KB" or "K" => 1024L,
                    "B" or "BYTE" or "BYTES" => 1L,
                    _ => 1L
                };

                return (long)Math.Round(parsedValue * multiplier);
            }
        }

        // Clean integer formats: remove spaces, dots or commas used as thousand separators
        var digitsOnly = Regex.Replace(trimmed, @"[^\d]", string.Empty);
        return long.TryParse(digitsOnly, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) ? size : 0L;
    }

    /// <summary>
    /// Normalizes a date string to "yyyy-MM-dd HH:mm:ss". Slash-separated dates parse
    /// month-first (en-US); dot-separated dates parse day-first (de-DE); anything else
    /// falls back to the invariant culture. Unparseable values round-trip unchanged.
    /// </summary>
    public static string NormalizeDate(string raw)
    {
        var trimmed = raw.Trim();

        if (trimmed.Contains('/') && DateTimeOffset.TryParse(trimmed, new CultureInfo("en-US"), DateTimeStyles.None, out var us))
        {
            return us.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        if (trimmed.Contains('.') && DateTimeOffset.TryParse(trimmed, new CultureInfo("de-DE"), DateTimeStyles.None, out var eu))
        {
            return eu.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
        {
            return dto.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        return trimmed;
    }

    private static bool IsFolderKind(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return false;
        var lower = kind.Trim().ToLowerInvariant();
        return lower is "file folder" or "folder" or "directory" or "ordner" or "dossier" or "carpeta" or "フォルダ" or "папка";
    }

    private static string? GetValue(List<string> row, Dictionary<DiskCatalogMakerColumn, int> map, DiskCatalogMakerColumn col)
    {
        if (!map.TryGetValue(col, out var index) || index < 0 || index >= row.Count)
        {
            return null;
        }

        var value = row[index];
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
