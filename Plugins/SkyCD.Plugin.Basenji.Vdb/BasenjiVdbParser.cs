using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Data.Sqlite;
using SkyCD.Plugin.Basenji.Vdb.Exceptions;

namespace SkyCD.Plugin.Basenji.Vdb;

/// <summary>
/// Parser for Basenji .vdb (Volume Database) SQLite catalogs.
/// </summary>
public static class BasenjiVdbParser
{
    public const int MaxSupportedVersion = 4;
    private static readonly byte[] SqliteHeader = Encoding.ASCII.GetBytes("SQLite format 3\0");

    public static BasenjiVdbReportDocument Parse(
        Stream stream,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        progress?.Report(5);
        cancellationToken.ThrowIfCancellationRequested();

        ValidateSqliteHeader(stream);

        var tempFilePath = Path.Combine(Path.GetTempPath(), $"skycd-basenji-{Guid.NewGuid():N}.vdb");
        try
        {
            using (var fileStream = File.Create(tempFilePath))
            {
                stream.CopyTo(fileStream);
            }

            progress?.Report(20);
            cancellationToken.ThrowIfCancellationRequested();

            var connectionStringBuilder = new SqliteConnectionStringBuilder
            {
                DataSource = tempFilePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            };

            using var connection = new SqliteConnection(connectionStringBuilder.ConnectionString);
            connection.Open();

            progress?.Report(30);
            cancellationToken.ThrowIfCancellationRequested();

            ValidateSchema(connection);

            var (dbName, dbDesc, dbCreated, dbVersion, dbGuid) = ReadDatabaseProperties(connection);

            if (dbVersion > MaxSupportedVersion)
            {
                throw new BasenjiUnsupportedVersionException(dbVersion.ToString(CultureInfo.InvariantCulture));
            }

            progress?.Report(40);
            cancellationToken.ThrowIfCancellationRequested();

            var volumes = ReadVolumes(connection);
            if (volumes.Count == 0)
            {
                throw new BasenjiEmptyCatalogException();
            }

            progress?.Report(60);
            cancellationToken.ThrowIfCancellationRequested();

            var items = ReadItems(connection);

            progress?.Report(80);
            cancellationToken.ThrowIfCancellationRequested();

            var (rows, summary) = BuildDocument(dbName, dbDesc, dbVersion, dbGuid, volumes, items);

            progress?.Report(100);
            return new BasenjiVdbReportDocument(rows, summary);
        }
        catch (SqliteException ex)
        {
            throw new BasenjiCorruptFileException(ex.Message);
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
        var initialPosition = stream.CanSeek ? stream.Position : -1;
        var header = new byte[16];
        var read = stream.Read(header, 0, 16);
        if (read < 16)
        {
            throw new BasenjiCorruptFileException("File is empty or truncated.");
        }

        for (var i = 0; i < SqliteHeader.Length; i++)
        {
            if (header[i] != SqliteHeader[i])
            {
                throw new BasenjiCorruptFileException("File is not a valid SQLite database.");
            }
        }

        if (stream.CanSeek)
        {
            stream.Position = initialPosition;
        }
        else
        {
            throw new BasenjiCorruptFileException("Stream does not support seeking.");
        }
    }

    private static void ValidateSchema(SqliteConnection connection)
    {
        var requiredTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DatabaseProperties",
            "Volumes",
            "Items"
        };

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table';";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var tableName = reader.GetString(0);
            requiredTables.Remove(tableName);
        }

        if (requiredTables.Count > 0)
        {
            throw new BasenjiCorruptFileException($"Database is missing required Basenji tables: {string.Join(", ", requiredTables)}");
        }
    }

    private static (string Name, string Description, string Created, int Version, string Guid) ReadDatabaseProperties(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name, Description, Created, Version, GUID FROM DatabaseProperties LIMIT 1;";

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new BasenjiCorruptFileException("DatabaseProperties table is empty.");
        }

        var name = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
        var description = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        var created = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
        var version = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
        var guid = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);

        return (name, description, created, version, guid);
    }

    private sealed record VolumeRecord(
        long VolumeId,
        string Title,
        string Added,
        int VolumeType,
        bool IsHashed,
        string ArchiveNr,
        string DriveType,
        string LoanedTo,
        string LoanedDate,
        string ReturnDate,
        string Category,
        string Description,
        string Keywords,
        long Files,
        long Dirs,
        long Size);

    private static List<VolumeRecord> ReadVolumes(SqliteConnection connection)
    {
        var list = new List<VolumeRecord>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT VolumeID, Title, Added, VolumeType, IsHashed, ArchiveNr, DriveType, Loaned_To, Loaned_Date, Return_Date, Category, Description, Keywords, Files, Dirs, Size FROM Volumes ORDER BY VolumeID;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new VolumeRecord(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                !reader.IsDBNull(4) && reader.GetInt32(4) != 0,
                reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
                reader.IsDBNull(13) ? 0 : reader.GetInt64(13),
                reader.IsDBNull(14) ? 0 : reader.GetInt64(14),
                reader.IsDBNull(15) ? 0 : reader.GetInt64(15)
            ));
        }

        return list;
    }

    private sealed record ItemRecord(
        long VolumeId,
        long ItemId,
        long ParentId,
        int ItemType,
        string Name,
        string MimeType,
        string MetaData,
        string Note,
        string Keywords,
        string Hash,
        bool IsContainer,
        string Location,
        string LastWriteTime,
        long SymLinkTargetId,
        long Size);

    private static List<ItemRecord> ReadItems(SqliteConnection connection)
    {
        var list = new List<ItemRecord>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT VolumeID, ItemID, ParentID, ItemType, Name, MimeType, MetaData, Note, Keywords, Hash, IsContainer, Location, LastWriteTime, SymLinkTargetID, Size FROM Items ORDER BY VolumeID, ItemID;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new ItemRecord(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.IsDBNull(2) ? -1 : reader.GetInt64(2),
                reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                !reader.IsDBNull(10) && reader.GetInt32(10) != 0,
                reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
                reader.IsDBNull(13) ? -1 : reader.GetInt64(13),
                reader.IsDBNull(14) ? 0 : reader.GetInt64(14)
            ));
        }

        return list;
    }

    private static (IReadOnlyList<Dictionary<string, object?>> Rows, BasenjiVdbImportSummary Summary) BuildDocument(
        string dbName,
        string dbDesc,
        int dbVersion,
        string dbGuid,
        List<VolumeRecord> volumes,
        List<ItemRecord> items)
    {
        var rows = new List<Dictionary<string, object?>>();
        var itemsByVolume = new Dictionary<long, List<ItemRecord>>();
        foreach (var item in items)
        {
            if (!itemsByVolume.TryGetValue(item.VolumeId, out var volumeItems))
            {
                volumeItems = [];
                itemsByVolume[item.VolumeId] = volumeItems;
            }
            volumeItems.Add(item);
        }

        var dirCount = 0;
        var fileCount = 0;
        var totalSizeBytes = 0L;

        foreach (var vol in volumes)
        {
            var volumeNodeId = $"media:{vol.VolumeId}";
            var volumeProps = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["VolumeId"] = vol.VolumeId.ToString(CultureInfo.InvariantCulture),
                ["VolumeType"] = vol.VolumeType == 1 ? "AudioCd" : "FileSystem",
                ["DriveType"] = MapDriveType(vol.DriveType),
                ["IsHashed"] = vol.IsHashed ? "true" : "false",
                ["FilesCount"] = vol.Files.ToString(CultureInfo.InvariantCulture),
                ["DirsCount"] = vol.Dirs.ToString(CultureInfo.InvariantCulture)
            };

            if (!string.IsNullOrWhiteSpace(vol.ArchiveNr))
            {
                volumeProps["ArchiveNr"] = vol.ArchiveNr;
            }

            if (!string.IsNullOrWhiteSpace(vol.Category))
            {
                volumeProps["Category"] = vol.Category;
            }

            if (!string.IsNullOrWhiteSpace(vol.Description))
            {
                volumeProps["Description"] = vol.Description;
                volumeProps["Comment"] = vol.Description;
            }

            if (!string.IsNullOrWhiteSpace(vol.Keywords))
            {
                volumeProps["Keywords"] = vol.Keywords;
            }

            if (!string.IsNullOrWhiteSpace(vol.LoanedTo))
            {
                volumeProps["LoanedTo"] = vol.LoanedTo;
                volumeProps["Borrow"] = vol.LoanedTo;
                volumeProps["Loan"] = vol.LoanedTo;
            }

            if (!string.IsNullOrWhiteSpace(vol.LoanedDate))
            {
                volumeProps["LoanedDate"] = vol.LoanedDate;
            }

            if (!string.IsNullOrWhiteSpace(vol.ReturnDate))
            {
                volumeProps["ReturnDate"] = vol.ReturnDate;
            }

            if (!string.IsNullOrWhiteSpace(vol.Added))
            {
                volumeProps["DateAdded"] = vol.Added;
                volumeProps["DateCreated"] = vol.Added;
                volumeProps["DateModified"] = vol.Added;
            }

            if (vol.VolumeType == 1)
            {
                volumeProps["Tracks"] = vol.Files.ToString(CultureInfo.InvariantCulture);
                volumeProps["Duration"] = vol.Size.ToString(CultureInfo.InvariantCulture);
            }

            var volSize = vol.VolumeType == 1 ? 0L : vol.Size;

            var volumeRow = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["nodeId"] = volumeNodeId,
                ["parentId"] = null,
                ["kind"] = "media",
                ["name"] = string.IsNullOrWhiteSpace(vol.Title) ? $"Volume {vol.VolumeId}" : vol.Title,
                ["sizeBytes"] = volSize.ToString(CultureInfo.InvariantCulture),
                ["properties"] = volumeProps
            };

            rows.Add(volumeRow);

            if (!itemsByVolume.TryGetValue(vol.VolumeId, out var volumeItemList))
            {
                continue;
            }

            long rootItemId = -1;
            foreach (var item in volumeItemList)
            {
                if (item.ParentId == -1)
                {
                    rootItemId = item.ItemId;
                    if (!string.IsNullOrWhiteSpace(item.Note))
                    {
                        volumeProps["Note"] = item.Note;
                    }
                    if (!string.IsNullOrWhiteSpace(item.Keywords) && !volumeProps.ContainsKey("Keywords"))
                    {
                        volumeProps["Keywords"] = item.Keywords;
                    }
                    if (!string.IsNullOrWhiteSpace(item.MetaData))
                    {
                        BasenjiMetadataExtractor.ExtractMetadata(item.MetaData, volumeProps);
                    }
                    break;
                }
            }

            foreach (var item in volumeItemList)
            {
                if (item.ItemId == rootItemId && item.ParentId == -1)
                {
                    continue; // Skip root item as it maps to the volume media node
                }

                var isFolder = item.ItemType == 0 || item.ItemType == 2 || item.IsContainer;
                if (isFolder)
                {
                    dirCount++;
                }
                else
                {
                    fileCount++;
                    totalSizeBytes += item.Size;
                }

                var itemNodeId = $"item:{vol.VolumeId}_{item.ItemId}";
                var parentNodeId = (item.ParentId == -1 || item.ParentId == rootItemId)
                    ? volumeNodeId
                    : $"item:{vol.VolumeId}_{item.ParentId}";

                var itemProps = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ItemId"] = item.ItemId.ToString(CultureInfo.InvariantCulture),
                    ["VolumeId"] = vol.VolumeId.ToString(CultureInfo.InvariantCulture)
                };

                if (!string.IsNullOrWhiteSpace(item.MimeType))
                {
                    itemProps["MimeType"] = item.MimeType;
                }

                if (!string.IsNullOrWhiteSpace(item.Note))
                {
                    itemProps["Note"] = item.Note;
                    itemProps["Comment"] = item.Note;
                }

                if (!string.IsNullOrWhiteSpace(item.Keywords))
                {
                    itemProps["Keywords"] = item.Keywords;
                }

                if (!string.IsNullOrWhiteSpace(item.Hash))
                {
                    itemProps["Hash"] = item.Hash;
                    itemProps["Checksum"] = item.Hash;
                }

                if (!string.IsNullOrWhiteSpace(item.Location))
                {
                    itemProps["Location"] = item.Location;
                }

                if (!string.IsNullOrWhiteSpace(item.LastWriteTime))
                {
                    itemProps["DateModified"] = item.LastWriteTime;
                    itemProps["DateCreated"] = item.LastWriteTime;
                }

                if (item.ItemType == 3 && item.SymLinkTargetId >= 0)
                {
                    itemProps["SymLinkTargetId"] = item.SymLinkTargetId.ToString(CultureInfo.InvariantCulture);
                }

                if (item.ItemType == 4) // Audio track
                {
                    itemProps["Duration"] = item.Size.ToString(CultureInfo.InvariantCulture);
                }

                if (!string.IsNullOrWhiteSpace(item.MetaData))
                {
                    BasenjiMetadataExtractor.ExtractMetadata(item.MetaData, itemProps);
                }

                var itemSize = (item.ItemType == 3) ? 0L : item.Size;

                var itemRow = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["nodeId"] = itemNodeId,
                    ["parentId"] = parentNodeId,
                    ["kind"] = isFolder ? "folder" : "file",
                    ["name"] = string.IsNullOrWhiteSpace(item.Name) ? $"Item {item.ItemId}" : item.Name,
                    ["sizeBytes"] = itemSize.ToString(CultureInfo.InvariantCulture),
                    ["properties"] = itemProps
                };

                rows.Add(itemRow);
            }
        }

        var summary = new BasenjiVdbImportSummary
        {
            DatabaseName = dbName,
            DatabaseDescription = dbDesc,
            DatabaseVersion = dbVersion,
            DatabaseGuid = dbGuid,
            MediaCount = volumes.Count,
            DirectoryCount = dirCount,
            FileCount = fileCount,
            TotalSizeBytes = totalSizeBytes
        };

        return (rows, summary);
    }

    private static string MapDriveType(string rawType) =>
        rawType.ToLowerInvariant() switch
        {
            "cd" or "cdrom" or "dvd" or "optical" => "CDRom",
            "fixed" or "harddisc" or "harddisk" or "hdd" => "Fixed",
            "network" or "networkplace" or "net" => "Network",
            "floppy" or "flashdrive" or "usb" or "flash" or "removable" or "other" => "Removable",
            _ => "Unknown"
        };
}
