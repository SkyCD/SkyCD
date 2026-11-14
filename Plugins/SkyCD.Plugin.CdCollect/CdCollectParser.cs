using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Data.Sqlite;
using SkyCD.Plugin.CdCollect.Exceptions;

namespace SkyCD.Plugin.CdCollect;

/// <summary>
/// Parser for CDcollect SQLite database catalogs.
/// </summary>
public static class CdCollectParser
{
    private static readonly byte[] SqliteHeader = "SQLite format 3\0"u8.ToArray();

    public static CdCollectReportDocument Parse(
        Stream stream,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        progress?.Report(5);
        cancellationToken.ThrowIfCancellationRequested();

        ValidateSqliteHeader(stream);

        var tempFilePath = Path.Combine(Path.GetTempPath(), $"cdcollect_{Guid.NewGuid():N}.db");
        try
        {
            CopyStreamToFile(stream, tempFilePath, cancellationToken);
            progress?.Report(20);
            cancellationToken.ThrowIfCancellationRequested();

            return ParseDatabaseFile(tempFilePath, progress, cancellationToken);
        }
        finally
        {
            try
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
            catch
            {
                // Ignore cleanup errors on temp file
            }
        }
    }

    private static void ValidateSqliteHeader(Stream stream)
    {
        if (!stream.CanRead)
        {
            throw new CdCollectCorruptDatabaseException("Stream is not readable.");
        }

        var initialPos = stream.CanSeek ? stream.Position : -1;
        try
        {
            var header = new byte[16];
            var bytesRead = stream.Read(header, 0, header.Length);
            if (bytesRead < 16)
            {
                throw new CdCollectCorruptDatabaseException("File is empty or truncated.");
            }

            for (var i = 0; i < 16; i++)
            {
                if (header[i] != SqliteHeader[i])
                {
                    throw new CdCollectInvalidFormatException("Stream does not contain a valid SQLite 3 database header.");
                }
            }
        }
        finally
        {
            if (initialPos >= 0 && stream.CanSeek)
            {
                stream.Position = initialPos;
            }
        }
    }

    private static void CopyStreamToFile(Stream source, string destinationPath, CancellationToken cancellationToken)
    {
        if (source.CanSeek)
        {
            source.Position = 0;
        }

        using var dest = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        source.CopyTo(dest);
    }

    private static CdCollectReportDocument ParseDatabaseFile(
        string databasePath,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<string>();
        var unsupportedFeatures = new List<string>();

        var connectionStringBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        };

        using var connection = new SqliteConnection(connectionStringBuilder.ConnectionString);
        try
        {
            connection.Open();
        }
        catch (Exception ex)
        {
            throw new CdCollectCorruptDatabaseException($"Failed to open SQLite database: {ex.Message}");
        }

        var version = GetDatabaseVersion(connection);
        if (version > 4)
        {
            diagnostics.Add($"Database version {version} is newer than known CDcollect version 4; parsing in compatibility mode.");
        }

        progress?.Report(35);
        cancellationToken.ThrowIfCancellationRequested();

        var infoPluginsCount = CountTableRows(connection, "infoplugins");
        var compPluginsCount = CountTableRows(connection, "compplugins");

        var cds = ReadCds(connection);
        if (cds.Count == 0)
        {
            throw new CdCollectEmptyCatalogException();
        }

        progress?.Report(50);
        cancellationToken.ThrowIfCancellationRequested();

        var (filesByCd, filesTableName) = ReadFiles(connection, diagnostics);

        progress?.Report(75);
        cancellationToken.ThrowIfCancellationRequested();

        var rows = BuildCatalogRows(cds, filesByCd, out var totalSize, out var totalDirs, out var totalFiles);

        progress?.Report(100);

        var summary = new CdCollectImportSummary(
            CatalogVersion: version,
            MediaCount: cds.Count,
            DirectoryCount: totalDirs,
            FileCount: totalFiles,
            TotalSizeBytes: totalSize,
            InfoPluginsCount: infoPluginsCount,
            CompPluginsCount: compPluginsCount,
            Diagnostics: diagnostics,
            UnsupportedFeatures: unsupportedFeatures);

        return new CdCollectReportDocument(rows, summary);
    }

    private static int GetDatabaseVersion(SqliteConnection connection)
    {
        if (!TableExists(connection, "cdcollect_version"))
        {
            return 0;
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT ver FROM cdcollect_version LIMIT 1;";
        try
        {
            var result = cmd.ExecuteScalar();
            if (result != null && result != DBNull.Value && int.TryParse(result.ToString(), out var ver))
            {
                return ver;
            }
            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=@name;";
        cmd.Parameters.AddWithValue("@name", tableName);
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        return count > 0;
    }

    private static int CountTableRows(SqliteConnection connection, string tableName)
    {
        if (!TableExists(connection, tableName))
        {
            return 0;
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT count(*) FROM \"{tableName}\";";
        try
        {
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch
        {
            return 0;
        }
    }

    private sealed record CdRecord(int Id, string Name, string? Keywords, int Year, int Month, int Day, string? Icon);

    private static List<CdRecord> ReadCds(SqliteConnection connection)
    {
        if (!TableExists(connection, "cds"))
        {
            throw new CdCollectInvalidFormatException("Required 'cds' table not found in database.");
        }

        var cds = new List<CdRecord>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, keywords, year, month, day, icon FROM cds ORDER BY id;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt32(0);
            var name = reader.IsDBNull(1) ? $"CD {id}" : reader.GetString(1);
            var keywords = reader.IsDBNull(2) ? null : reader.GetString(2);
            var year = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
            var month = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);
            var day = reader.IsDBNull(5) ? 0 : reader.GetInt32(5);
            var icon = reader.IsDBNull(6) ? null : reader.GetString(6);

            cds.Add(new CdRecord(id, name, keywords, year, month, day, icon));
        }

        return cds;
    }

    private sealed record FileRecord(
        int CdId,
        string PathKey,
        string Name,
        string? Info,
        long Size,
        string? Mime,
        string? Location,
        int Type,
        bool Packed);

    private static (Dictionary<int, List<FileRecord>> filesByCd, string? tableName) ReadFiles(
        SqliteConnection connection,
        List<string> diagnostics)
    {
        var result = new Dictionary<int, List<FileRecord>>();
        string? tableName = null;

        if (TableExists(connection, "files_v1"))
        {
            tableName = "files_v1";
        }
        else if (TableExists(connection, "files"))
        {
            tableName = "files";
            diagnostics.Add("Catalog uses legacy 'files' table format (version 0).");
        }

        if (tableName == null)
        {
            return (result, null);
        }

        using var cmd = connection.CreateCommand();
        if (tableName == "files_v1")
        {
            cmd.CommandText = "SELECT cd_id, path, name, info, size, mime, location, type, packed FROM files_v1 ORDER BY cd_id, path;";
        }
        else
        {
            cmd.CommandText = "SELECT cd_id, path, name, info, size, mime, '' AS location, type, packed FROM files ORDER BY cd_id, path;";
        }

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var cdId = reader.GetInt32(0);
            var pathKey = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            var name = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            var info = reader.IsDBNull(3) ? null : reader.GetString(3);
            var size = reader.IsDBNull(4) ? 0L : Convert.ToInt64(reader.GetValue(4), CultureInfo.InvariantCulture);
            var mime = reader.IsDBNull(5) ? null : reader.GetString(5);
            var location = reader.IsDBNull(6) ? null : reader.GetString(6);
            var type = reader.IsDBNull(7) ? 1 : Convert.ToInt32(reader.GetValue(7), CultureInfo.InvariantCulture);
            var packedInt = reader.IsDBNull(8) ? 0 : Convert.ToInt32(reader.GetValue(8), CultureInfo.InvariantCulture);
            var packed = packedInt != 0;

            if (!result.TryGetValue(cdId, out var list))
            {
                list = [];
                result[cdId] = list;
            }

            list.Add(new FileRecord(cdId, pathKey, name, info, size, mime, location, type, packed));
        }

        return (result, tableName);
    }

    private static List<Dictionary<string, object?>> BuildCatalogRows(
        List<CdRecord> cds,
        Dictionary<int, List<FileRecord>> filesByCd,
        out long totalSize,
        out int totalDirs,
        out int totalFiles)
    {
        totalSize = 0L;
        totalDirs = 0;
        totalFiles = 0;

        var rows = new List<Dictionary<string, object?>>();

        foreach (var cd in cds)
        {
            var cdIdStr = $"cd_{cd.Id}";
            var cdProps = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Id"] = cd.Id.ToString(CultureInfo.InvariantCulture),
                ["VolumeNumber"] = cd.Id.ToString(CultureInfo.InvariantCulture),
                ["DriveType"] = "CDRom"
            };

            var dateStr = CdCollectMetadataExtractor.FormatDate(cd.Year, cd.Month, cd.Day);
            if (dateStr != null)
            {
                cdProps["DateAdded"] = dateStr;
                cdProps["DateCreated"] = dateStr;
                cdProps["Date"] = dateStr;
            }

            if (!string.IsNullOrWhiteSpace(cd.Keywords))
            {
                cdProps["Keywords"] = cd.Keywords;
                cdProps["Comments"] = cd.Keywords;
            }

            if (!string.IsNullOrWhiteSpace(cd.Icon))
            {
                cdProps["Icon"] = cd.Icon;
            }

            filesByCd.TryGetValue(cd.Id, out var cdFiles);
            cdFiles ??= [];

            // Build hierarchy from file records
            // In CDcollect:
            // PathKey is either a colon-delimited node index sequence (e.g. "0", "0:1", "0:1:5")
            // or location is a directory path ("/Folder/Subfolder").
            // We map each file's parent by its PathKey prefix or location.
            var cdRows = BuildCdSubtree(cdIdStr, cd.Name, cdProps, cdFiles, ref totalDirs, ref totalFiles, out var cdSize);
            totalSize += cdSize;
            rows.AddRange(cdRows);
        }

        return rows;
    }

    private static List<Dictionary<string, object?>> BuildCdSubtree(
        string cdIdStr,
        string cdName,
        Dictionary<string, object?> cdProps,
        List<FileRecord> records,
        ref int totalDirs,
        ref int totalFiles,
        out long mediaSize)
    {
        var result = new List<Dictionary<string, object?>>();
        var pathToNodeId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // CD Root Node
        var cdRow = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Id"] = cdIdStr,
            ["ParentId"] = null,
            ["Type"] = "Media",
            ["Name"] = cdName,
            ["Size"] = 0L,
            ["Properties"] = cdProps
        };

        result.Add(cdRow);

        long calculatedCdSize = 0L;

        // Group / order records
        // If path keys are colon-separated like "0", "0:1", we sort by number of segments so parents are processed before children
        var sortedRecords = new List<FileRecord>(records);
        sortedRecords.Sort((a, b) =>
        {
            var aSegs = CountColons(a.PathKey);
            var bSegs = CountColons(b.PathKey);
            var cmp = aSegs.CompareTo(bSegs);
            return cmp != 0 ? cmp : string.Compare(a.PathKey, b.PathKey, StringComparison.Ordinal);
        });

        var nodeIndex = 0;
        foreach (var rec in sortedRecords)
        {
            nodeIndex++;
            var nodeId = $"{cdIdStr}_n{nodeIndex}";
            var isDirectory = rec.Type == 0;

            // Determine ParentId
            string parentId = cdIdStr;
            if (!string.IsNullOrEmpty(rec.PathKey))
            {
                var lastColon = rec.PathKey.LastIndexOf(':');
                if (lastColon > 0)
                {
                    var parentPathKey = rec.PathKey[..lastColon];
                    if (pathToNodeId.TryGetValue(parentPathKey, out var foundParentId))
                    {
                        parentId = foundParentId;
                    }
                }
                pathToNodeId[rec.PathKey] = nodeId;
            }

            var itemProps = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(rec.Mime))
            {
                itemProps["MimeType"] = rec.Mime;
                itemProps["Mime"] = rec.Mime;
            }

            if (!string.IsNullOrWhiteSpace(rec.Location))
            {
                itemProps["Location"] = rec.Location;
                itemProps["Path"] = rec.Location;
            }

            if (rec.Packed)
            {
                itemProps["IsPacked"] = "true";
                itemProps["Packed"] = "true";
            }

            CdCollectMetadataExtractor.ExtractInfoProperties(rec.Info, itemProps);

            var itemRow = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Id"] = nodeId,
                ["ParentId"] = parentId,
                ["Type"] = isDirectory ? "Folder" : "File",
                ["Name"] = string.IsNullOrEmpty(rec.Name) ? (isDirectory ? "New Folder" : "Unnamed") : rec.Name,
                ["Size"] = isDirectory ? 0L : rec.Size,
                ["Properties"] = itemProps
            };

            if (isDirectory)
            {
                totalDirs++;
            }
            else
            {
                totalFiles++;
                calculatedCdSize += rec.Size;
            }

            result.Add(itemRow);
        }

        cdRow["Size"] = calculatedCdSize;
        mediaSize = calculatedCdSize;

        return result;
    }

    private static int CountColons(string s)
    {
        var count = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == ':') count++;
        }
        return count;
    }
}
