using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SkyCD.Plugin.WinCatalog.Csv.Exceptions;

namespace SkyCD.Plugin.WinCatalog.Csv;

/// <summary>
/// Reconstructs hierarchical catalog entries and metadata from WinCatalog CSV report rows.
/// </summary>
public static class WinCatalogHierarchyBuilder
{
    public static WinCatalogCsvReportDocument Build(
        List<List<string>> rawRows,
        char delimiter)
    {
        if (rawRows.Count == 0)
        {
            throw new WinCatalogCorruptHeaderException();
        }

        var headerRow = rawRows[0];
        if (headerRow.Count == 0 || headerRow.All(string.IsNullOrWhiteSpace))
        {
            throw new WinCatalogCorruptHeaderException();
        }

        // Map column indices
        var columnMap = new Dictionary<WinCatalogCsvColumn, int>();
        var unsupportedFields = new List<string>();

        for (var i = 0; i < headerRow.Count; i++)
        {
            var header = headerRow[i];
            var col = WinCatalogCsvProfile.MapColumn(header);
            if (col != WinCatalogCsvColumn.Unknown)
            {
                columnMap.TryAdd(col, i);
            }
            else if (!string.IsNullOrWhiteSpace(header))
            {
                unsupportedFields.Add(header.Trim());
            }
        }

        // Verify essential path columns
        if (!columnMap.ContainsKey(WinCatalogCsvColumn.CatalogPath) &&
            !columnMap.ContainsKey(WinCatalogCsvColumn.FilePath))
        {
            throw new WinCatalogMissingPathColumnException();
        }

        var totalRecords = rawRows.Count - 1;
        var skippedReasons = new List<string>();
        var skippedRecordsCount = 0;
        var importedRecordsCount = 0;

        var nodesById = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        var explicitFolderPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var synthesizedFolderPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                    explicitFolderPaths,
                    synthesizedFolderPaths);

                importedRecordsCount++;
            }
            catch (Exception ex)
            {
                skippedRecordsCount++;
                skippedReasons.Add($"Row {rowIndex + 1}: {ex.Message}");
            }
        }

        // A report is partial if intermediate folders had to be synthesized (search results) or if any rows were skipped
        var isPartialReport = synthesizedFolderPaths.Count > 0 || skippedRecordsCount > 0;

        var summary = new WinCatalogCsvImportSummary(
            TotalRecordsRead: totalRecords,
            ImportedRecordsCount: importedRecordsCount,
            SkippedRecordsCount: skippedRecordsCount,
            IsPartialReport: isPartialReport,
            SkippedReasons: skippedReasons,
            UnsupportedFields: unsupportedFields,
            DetectedDelimiter: delimiter,
            DetectedColumns: headerRow);

        var rowsList = nodesById.Values.ToList();
        return new WinCatalogCsvReportDocument(rowsList, summary);
    }

    private static void ProcessRow(
        List<string> row,
        int rowIndex,
        Dictionary<WinCatalogCsvColumn, int> columnMap,
        Dictionary<string, Dictionary<string, object?>> nodesById,
        HashSet<string> explicitFolderPaths,
        HashSet<string> synthesizedFolderPaths)
    {
        var catalogPath = GetValue(row, columnMap, WinCatalogCsvColumn.CatalogPath);
        var filePath = GetValue(row, columnMap, WinCatalogCsvColumn.FilePath);
        var name = GetValue(row, columnMap, WinCatalogCsvColumn.Name);
        var filename = GetValue(row, columnMap, WinCatalogCsvColumn.Filename);
        var disk = GetValue(row, columnMap, WinCatalogCsvColumn.Disk);
        var volumeLabel = GetValue(row, columnMap, WinCatalogCsvColumn.VolumeLabel);
        var sizeText = GetValue(row, columnMap, WinCatalogCsvColumn.Size);
        var typeText = GetValue(row, columnMap, WinCatalogCsvColumn.Type);
        var createdText = GetValue(row, columnMap, WinCatalogCsvColumn.Created);
        var modifiedText = GetValue(row, columnMap, WinCatalogCsvColumn.Modified);
        var comments = GetValue(row, columnMap, WinCatalogCsvColumn.Comments);
        var tags = GetValue(row, columnMap, WinCatalogCsvColumn.Tags);
        var rating = GetValue(row, columnMap, WinCatalogCsvColumn.Rating);
        var md5 = GetValue(row, columnMap, WinCatalogCsvColumn.MD5);
        var sha256 = GetValue(row, columnMap, WinCatalogCsvColumn.SHA256);
        var blake2 = GetValue(row, columnMap, WinCatalogCsvColumn.Blake2);
        var crc32 = GetValue(row, columnMap, WinCatalogCsvColumn.CRC32);
        var location = GetValue(row, columnMap, WinCatalogCsvColumn.PhysicalLocation);

        if (string.IsNullOrWhiteSpace(catalogPath) && string.IsNullOrWhiteSpace(filePath))
        {
            throw new InvalidOperationException("Row does not specify Catalog Path or File Path.");
        }

        // Determine media name and relative path parts
        string mediaName;
        List<string> folderSegments;
        string leafName;
        var isExplicitFolder = IsFolderType(typeText);

        if (!string.IsNullOrWhiteSpace(catalogPath))
        {
            var unescapedPath = UnescapeWinCatalogTokens(catalogPath);
            var parts = unescapedPath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            if (parts.Count > 0 && (parts[0].Equals("Catalog", StringComparison.OrdinalIgnoreCase) ||
                                    parts[0].Equals("Katalog", StringComparison.OrdinalIgnoreCase)))
            {
                parts.RemoveAt(0);
            }

            if (!string.IsNullOrWhiteSpace(disk))
            {
                mediaName = disk.Trim();
            }
            else if (parts.Count > 0)
            {
                mediaName = parts[0];
                parts.RemoveAt(0);
            }
            else
            {
                mediaName = "Media";
            }

            // Determine leaf name vs folder segments
            var preferredName = !string.IsNullOrWhiteSpace(filename) ? filename.Trim() : name?.Trim();
            if (isExplicitFolder)
            {
                folderSegments = parts;
                leafName = folderSegments.Count > 0 ? folderSegments[^1] : (preferredName ?? mediaName);
                if (folderSegments.Count > 0)
                {
                    folderSegments.RemoveAt(folderSegments.Count - 1);
                }
            }
            else
            {
                if (parts.Count > 0 && !string.IsNullOrWhiteSpace(preferredName) &&
                    parts[^1].Equals(preferredName, StringComparison.OrdinalIgnoreCase))
                {
                    leafName = parts[^1];
                    parts.RemoveAt(parts.Count - 1);
                    folderSegments = parts;
                }
                else if (!string.IsNullOrWhiteSpace(preferredName))
                {
                    leafName = preferredName;
                    folderSegments = parts;
                }
                else if (parts.Count > 0)
                {
                    leafName = parts[^1];
                    parts.RemoveAt(parts.Count - 1);
                    folderSegments = parts;
                }
                else
                {
                    leafName = $"Item_{rowIndex}";
                    folderSegments = [];
                }
            }
        }
        else
        {
            // From FilePath
            var normalized = filePath!.Replace('/', '\\').Trim();
            var parts = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            if (!string.IsNullOrWhiteSpace(disk))
            {
                mediaName = disk.Trim();
            }
            else if (normalized.StartsWith(@"\\", StringComparison.Ordinal) && parts.Count >= 2)
            {
                mediaName = $@"\\{parts[0]}\{parts[1]}";
                parts.RemoveAt(0);
                parts.RemoveAt(0);
            }
            else if (parts.Count > 0 && parts[0].Contains(':'))
            {
                mediaName = parts[0];
                parts.RemoveAt(0);
            }
            else if (parts.Count > 0)
            {
                mediaName = parts[0];
                parts.RemoveAt(0);
            }
            else
            {
                mediaName = "Media";
            }

            var preferredName = !string.IsNullOrWhiteSpace(filename) ? filename.Trim() : name?.Trim();
            if (isExplicitFolder)
            {
                folderSegments = parts;
                leafName = folderSegments.Count > 0 ? folderSegments[^1] : (preferredName ?? mediaName);
                if (folderSegments.Count > 0)
                {
                    folderSegments.RemoveAt(folderSegments.Count - 1);
                }
            }
            else
            {
                if (parts.Count > 0 && !string.IsNullOrWhiteSpace(preferredName) &&
                    parts[^1].Equals(preferredName, StringComparison.OrdinalIgnoreCase))
                {
                    leafName = parts[^1];
                    parts.RemoveAt(parts.Count - 1);
                    folderSegments = parts;
                }
                else if (!string.IsNullOrWhiteSpace(preferredName))
                {
                    leafName = preferredName;
                    folderSegments = parts;
                }
                else if (parts.Count > 0)
                {
                    leafName = parts[^1];
                    parts.RemoveAt(parts.Count - 1);
                    folderSegments = parts;
                }
                else
                {
                    leafName = $"Item_{rowIndex}";
                    folderSegments = [];
                }
            }
        }

        // 1. Ensure Media Root exists
        var mediaId = $"media:{mediaName}";
        if (!nodesById.ContainsKey(mediaId))
        {
            var mediaProps = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(volumeLabel)) mediaProps["VolumeLabel"] = volumeLabel;
            if (!string.IsNullOrWhiteSpace(location)) mediaProps["PhysicalLocation"] = location;

            nodesById[mediaId] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["nodeId"] = mediaId,
                ["parentId"] = null,
                ["kind"] = "media",
                ["name"] = mediaName,
                ["sizeBytes"] = "0",
                ["properties"] = mediaProps
            };
        }

        // 2. Ensure intermediate folders exist
        var currentParentId = mediaId;
        var currentPath = mediaName;
        for (var i = 0; i < folderSegments.Count; i++)
        {
            var folderName = folderSegments[i];
            currentPath = $"{currentPath}/{folderName}";
            var folderId = $"folder:{currentPath}";

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
            var folderId = $"folder:{folderPath}";
            explicitFolderPaths.Add(folderPath);
            synthesizedFolderPaths.Remove(folderPath);

            var folderProperties = BuildProperties(catalogPath, filePath, createdText, modifiedText, comments, tags, rating, md5, sha256, blake2, crc32, location, volumeLabel);

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
            var fileId = $"file:{currentPath}/{leafName}";
            var fileProperties = BuildProperties(catalogPath, filePath, createdText, modifiedText, comments, tags, rating, md5, sha256, blake2, crc32, location, volumeLabel);

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

    private static Dictionary<string, object?> BuildProperties(
        string? catalogPath,
        string? filePath,
        string? createdText,
        string? modifiedText,
        string? comments,
        string? tags,
        string? rating,
        string? md5,
        string? sha256,
        string? blake2,
        string? crc32,
        string? location,
        string? volumeLabel)
    {
        var props = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(catalogPath)) props["WinCatalogPath"] = catalogPath;
        if (!string.IsNullOrWhiteSpace(filePath)) props["WinCatalogFilePath"] = filePath;
        if (!string.IsNullOrWhiteSpace(createdText)) props["DateCreated"] = NormalizeDate(createdText);
        if (!string.IsNullOrWhiteSpace(modifiedText)) props["DateModified"] = NormalizeDate(modifiedText);
        if (!string.IsNullOrWhiteSpace(comments)) props["Comments"] = comments;
        if (!string.IsNullOrWhiteSpace(tags)) props["Tags"] = tags;
        if (!string.IsNullOrWhiteSpace(rating)) props["Rating"] = rating;
        if (!string.IsNullOrWhiteSpace(md5)) props["MD5"] = md5;
        if (!string.IsNullOrWhiteSpace(sha256)) props["SHA256"] = sha256;
        if (!string.IsNullOrWhiteSpace(blake2)) props["Blake2"] = blake2;
        if (!string.IsNullOrWhiteSpace(crc32)) props["CRC32"] = crc32;
        if (!string.IsNullOrWhiteSpace(location)) props["PhysicalLocation"] = location;
        if (!string.IsNullOrWhiteSpace(volumeLabel)) props["VolumeLabel"] = volumeLabel;

        return props;
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

    public static string NormalizeDate(string raw)
    {
        var trimmed = raw.Trim();
        if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
        {
            return dto.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        // Try European format dd.MM.yyyy HH:mm:ss
        if (DateTimeOffset.TryParse(trimmed, new CultureInfo("de-DE"), DateTimeStyles.None, out dto))
        {
            return dto.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        return trimmed;
    }

    public static string UnescapeWinCatalogTokens(string path)
    {
        return path
            .Replace("{@slash}", "/", StringComparison.OrdinalIgnoreCase)
            .Replace("{@dslash}", "//", StringComparison.OrdinalIgnoreCase)
            .Replace("{@semicolon}", ";", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFolderType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return false;
        var lower = type.Trim().ToLowerInvariant();
        return lower is "folder" or "directory" or "ordner" or "dossier" or "carpeta" or "папка" or "cartella";
    }

    private static string? GetValue(List<string> row, Dictionary<WinCatalogCsvColumn, int> map, WinCatalogCsvColumn col)
    {
        if (map.TryGetValue(col, out var index) && index < row.Count)
        {
            var val = row[index];
            return string.IsNullOrWhiteSpace(val) ? null : val;
        }

        return null;
    }
}
