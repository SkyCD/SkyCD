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
using SkyCD.Plugin.CdCollect.Models;

namespace SkyCD.Plugin.CdCollect.Parsing;

/// <summary>
/// Parser for CDcollect SQLite database catalogs.
/// </summary>
public static class CdCollectParser
{
    private static readonly byte[] SqliteHeader = "SQLite format 3\0"u8.ToArray();

    public static CdCollectReportDocument Parse(
        Stream stream,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default,
        string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        progress?.Report(5);
        cancellationToken.ThrowIfCancellationRequested();


        var tempFilePath = Path.Combine(Path.GetTempPath(), $"cdcollect_{Guid.NewGuid():N}.db");
        try
        {
            CopyStreamToFile(stream, tempFilePath, cancellationToken);
            using (var headerStream = File.OpenRead(tempFilePath))
            {
                ValidateSqliteHeader(headerStream);
            }
            progress?.Report(20);
            cancellationToken.ThrowIfCancellationRequested();

            return ParseDatabaseFile(tempFilePath, progress, cancellationToken, sourcePath);
        }
        catch (SqliteException ex)
        {
            throw new CdCollectCorruptDatabaseException(ex.Message);
        }
        finally
        {
            try
            {
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
            var bytesRead = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
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
        var buffer = new byte[81920];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            dest.Write(buffer, 0, read);
        }
    }

    private static CdCollectReportDocument ParseDatabaseFile(
        string databasePath,
        IProgress<int>? progress,
        CancellationToken cancellationToken,
        string? sourcePath)
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

        if (!HasCatalogSchema(connection))
        {
            throw new CdCollectInvalidFormatException("Required catalog tables or columns are missing; expected a CDCollect cds3.db database.");
        }
        var version = GetDatabaseVersion(connection);
        if (version != 4 && !(version == 0 && !TableExists(connection, "files_v1")))
        {
            throw new CdCollectUnsupportedVersionException(version);
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

        var mediaIds = cds.Select(cd => cd.Id).ToHashSet();
        if (filesByCd.Keys.Any(id => !mediaIds.Contains(id)))
            throw new CdCollectCorruptDatabaseException("File entries reference missing media.");
        var rows = BuildCatalogRows(cds, filesByCd, out var totalSize, out var totalDirs, out var totalFiles);

        ImportThumbnails(rows, sourcePath, cancellationToken);
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
        cmd.CommandText = "SELECT ver FROM cdcollect_version;";
        using var reader = cmd.ExecuteReader();
        if (!reader.Read() || reader.IsDBNull(0) ||
            !int.TryParse(reader.GetValue(0).ToString(), out var version) || reader.Read())
        {
            throw new CdCollectCorruptDatabaseException("Expected exactly one integer schema version. Restore a valid version-4 backup.");
        }
        return version;
    }

    internal static bool HasCatalogSchema(SqliteConnection connection)
    {
        return HasColumns("cds", "id,name,keywords,year,month,day,icon") &&
            ((HasColumns("files_v1", "cd_id,path,name,info,size,mime,location,type,packed") &&
              HasColumns("cdcollect_version", "ver")) ||
             (!TableExists(connection, "files_v1") && HasColumns("files", "cd_id,path,name,info,size,mime,type,packed")));

        bool HasColumns(string table, string required)
        {
            if (!TableExists(connection, table)) return false;
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({table});";
            using var reader = command.ExecuteReader();
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read()) columns.Add(reader.GetString(1));
            return required.Split(',').All(columns.Contains);
        }
    }

    private static void ImportThumbnails(List<Dictionary<string, object?>> rows, string? sourcePath, CancellationToken token)
    {
        if (string.IsNullOrEmpty(sourcePath)) return;
        var directory = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        foreach (var row in rows)
        {
            token.ThrowIfCancellationRequested();
            var props = (Dictionary<string, object?>)row["Properties"]!;
            if (!props.TryGetValue("CdCollectPath", out var path)) continue;
            var key = path!.ToString()!;
            // Only catalog indices may select a sidecar, never stored filesystem paths.
            if (key.Length == 0 || key.Any(c => c != ':' && (c < '0' || c > '9'))) continue;
            var media = props["CdCollectMediaId"]!.ToString()!;
            var thumbnail = Path.Combine(directory, "thumbs", media, $"{media}_{key}.png");
            if (!File.Exists(thumbnail) || new FileInfo(thumbnail).Length > 8 * 1024 * 1024) continue;
            var bytes = File.ReadAllBytes(thumbnail);
            if (bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                props["Thumbnail"] = "data:image/png;base64," + Convert.ToBase64String(bytes);
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
            if (rec.Type is not (0 or 1) || rec.Size < 0 ||
                rec.PathKey.Length == 0 || pathToNodeId.ContainsKey(rec.PathKey))
                throw new CdCollectCorruptDatabaseException("Invalid entry type, size, or duplicate/empty hierarchy key.");
            var isDirectory = rec.Type == 0;

            // Determine ParentId
            string parentId = cdIdStr;
            if (!string.IsNullOrEmpty(rec.PathKey))
            {
                var lastColon = rec.PathKey.LastIndexOf(':');
                if (lastColon > 0)
                {
                    var parentPathKey = rec.PathKey[..lastColon];
                    if (!pathToNodeId.TryGetValue(parentPathKey, out var foundParentId))
                        throw new CdCollectCorruptDatabaseException($"Missing hierarchy parent '{parentPathKey}'.");
                    parentId = foundParentId;
                }
                pathToNodeId[rec.PathKey] = nodeId;
            }

            var itemProps = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["CdCollectPath"] = rec.PathKey,
                ["CdCollectMediaId"] = rec.CdId.ToString(CultureInfo.InvariantCulture)
            };
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
            itemProps["Info"] = rec.Info ?? string.Empty;

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
