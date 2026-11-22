using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SkyCD.Plugin.FileFormat.Katalog.Sqlite.Exceptions;
using SkyCD.Plugin.FileFormat.Katalog.Models;
using SkyCD.Plugin.FileFormat.Katalog.Sqlite.Models;

namespace SkyCD.Plugin.FileFormat.Katalog.Sqlite.Parsing;

/// <summary>
/// Reads a Katalog File-mode SQLite collection from a consistent read-only
/// snapshot. The source stream is materialized to a temp file (with -wal/-shm
/// sidecars when the source path is known), opened read-only with query-only
/// enforcement, and never modified. Schema variations are tolerated by probing
/// actual columns; only missing core tables or a pre-2.6 schema version fail.
/// </summary>
public static class KatalogSqliteReader
{
    private const int CancelCheckEveryRows = 500;

    public sealed record Snapshot(
        Dictionary<string, string> Parameters,
        List<KatalogDeviceRecord> Devices,
        List<KatalogStorageRecord> Storages,
        List<KatalogTagRecord> Tags,
        List<KatalogIdxCatalog> Catalogs,
        Dictionary<string, List<string>> CatalogFolders,
        string? SchemaVersion,
        List<string> Diagnostics);

    public static async Task<Snapshot> ReadAsync(
        Stream source,
        string? sourcePath,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var snapshotPath = Path.Combine(
            Path.GetTempPath(), $"skycd-katalog-{Guid.NewGuid():N}.db");
        try
        {
            using (var fileStream = File.Create(snapshotPath))
            {
                await source.CopyToAsync(fileStream, cancellationToken);
            }

            CopySidecar(sourcePath, snapshotPath, "-wal");
            CopySidecar(sourcePath, snapshotPath, "-shm");
            progress?.Report(15);

            return await ReadSnapshotAsync(snapshotPath, progress, cancellationToken);
        }
        finally
        {
            DeleteQuietly(snapshotPath);
            DeleteQuietly(snapshotPath + "-wal");
            DeleteQuietly(snapshotPath + "-shm");
        }
    }

    private static async Task<Snapshot> ReadSnapshotAsync(
        string snapshotPath,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<string>();
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = snapshotPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ConnectionString;

        try
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA query_only = ON";
                await pragma.ExecuteNonQueryAsync(cancellationToken);
            }

            progress?.Report(25);
            cancellationToken.ThrowIfCancellationRequested();

            var tables = await ReadTableNamesAsync(connection, cancellationToken);
            foreach (var required in KatalogSqliteSchema.RequiredTables)
            {
                if (!tables.Contains(required))
                {
                    throw new KatalogSqliteUnsupportedSchemaException(
                        $"required table '{required}' is missing. This is not a Katalog File-mode collection database.");
                }
            }

            var columns = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var table in KatalogSqliteSchema.RequiredTables)
            {
                columns[table] = await ReadColumnNamesAsync(connection, table, cancellationToken);
            }

            foreach (var (table, required) in KatalogSqliteSchema.RequiredColumns)
            {
                foreach (var column in required)
                {
                    if (!columns[table].Contains(column))
                    {
                        throw new KatalogSqliteUnsupportedSchemaException(
                            $"table '{table}' lacks required column '{column}'.");
                    }
                }
            }

            var hasStorage = tables.Contains(KatalogSqliteSchema.StorageTable);
            if (!hasStorage)
            {
                diagnostics.Add("Optional 'storage' table is absent; device storage details are unavailable.");
            }
            else
            {
                columns[KatalogSqliteSchema.StorageTable] =
                    await ReadColumnNamesAsync(connection, KatalogSqliteSchema.StorageTable, cancellationToken);
            }

            var parameters = await ReadParametersAsync(connection, cancellationToken);
            progress?.Report(35);

            var schemaVersion = parameters.TryGetValue("collection:version", out var version) ? version : null;
            if (string.IsNullOrWhiteSpace(schemaVersion))
            {
                diagnostics.Add("Schema version parameter (collection:version) is absent; proceeding with column-tolerant reads.");
            }
            else if (Version.TryParse(schemaVersion.Trim(), out var parsed)
                && parsed < KatalogSqliteSchema.MinimumSchemaVersion)
            {
                throw new KatalogSqliteUnsupportedSchemaException(
                    $"schema version '{schemaVersion}' predates the supported File-mode layout (minimum {KatalogSqliteSchema.MinimumSchemaVersion}).");
            }
            else if (!Version.TryParse(schemaVersion.Trim(), out _))
            {
                diagnostics.Add($"Schema version '{schemaVersion}' is not a recognized version number; proceeding with column-tolerant reads.");
            }

            var devices = await ReadDevicesAsync(connection, columns[KatalogSqliteSchema.DeviceTable], cancellationToken);
            progress?.Report(50);
            var storages = hasStorage
                ? await ReadStoragesAsync(connection, columns[KatalogSqliteSchema.StorageTable], cancellationToken)
                : [];
            progress?.Report(60);
            var tags = await ReadTagsAsync(connection, columns[KatalogSqliteSchema.TagTable], cancellationToken);
            progress?.Report(70);
            var availableFileColumns = columns[KatalogSqliteSchema.FileTable];
            var catalogs = await ReadCatalogsAsync(connection, devices, columns, availableFileColumns, cancellationToken);
            progress?.Report(80);

            if (catalogs.Count == 0)
            {
                throw new KatalogSqliteEmptyCollectionException();
            }

            foreach (var catalog in catalogs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ReadCatalogFilesAsync(connection, catalog, availableFileColumns, cancellationToken);
            }

            progress?.Report(90);
            var catalogFolders = await ReadCatalogFoldersAsync(connection, catalogs, cancellationToken);
            progress?.Report(100);

            diagnostics.Add($"Detected schema version '{schemaVersion ?? "unknown"}'; " +
                "statistics, search history, backup mappings, and catalog filters are not imported.");

            return new Snapshot(parameters, devices, storages, tags, catalogs, catalogFolders, schemaVersion, diagnostics);
        }
        catch (SqliteException exception)
        {
            throw new KatalogSqliteCorruptException(exception.Message);
        }
    }

    private static void CopySidecar(string? sourcePath, string snapshotPath, string suffix)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return;
        }

        try
        {
            var sidecar = sourcePath + suffix;
            if (File.Exists(sidecar))
            {
                File.Copy(sidecar, snapshotPath + suffix, overwrite: true);
            }
        }
        catch (IOException)
        {
            // Best effort: the main file alone still yields a consistent older snapshot.
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static async Task<HashSet<string>> ReadTableNamesAsync(
        SqliteConnection connection, CancellationToken cancellationToken)
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static async Task<HashSet<string>> ReadColumnNamesAsync(
        SqliteConnection connection, string table, CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({QuoteIdentifier(table)})";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string ReadText(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? string.Empty
            : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty;

    private static int ReadInt(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return 0;
        }

        return reader.GetValue(ordinal) switch
        {
            int number => number,
            long number => number > int.MaxValue ? int.MaxValue : (int)number,
            double number => (int)number,
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0
        };
    }

    private static long ReadLong(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return 0;
        }

        return reader.GetValue(ordinal) switch
        {
            long number => number,
            int number => number,
            double number => (long)number,
            string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0
        };
    }

    private static async Task<Dictionary<string, string>> ReadParametersAsync(
        SqliteConnection connection, CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT parameter_name, parameter_type, parameter_value1 FROM parameter";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = ReadText(reader, 0).Trim();
            var type = ReadText(reader, 1).Trim();
            var value = ReadText(reader, 2);
            if (!string.IsNullOrWhiteSpace(name))
            {
                parameters[$"{type}:{name}"] = value;
            }
        }

        return parameters;
    }

    private static async Task<List<KatalogDeviceRecord>> ReadDevicesAsync(
        SqliteConnection connection, HashSet<string> available, CancellationToken cancellationToken)
    {
        var select = new List<string>
        {
            "device_id", "device_parent_id", "device_name", "device_type", "device_external_id", "device_path"
        };
        foreach (var optional in new[]
            {
                "device_total_file_size", "device_total_file_count", "device_total_space",
                "device_free_space", "device_active", "device_group_id", "device_date_updated",
                "device_order", "device_comment"
            })
        {
            if (available.Contains(optional))
            {
                select.Add(optional);
            }
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {string.Join(", ", select)} FROM device ORDER BY device_id";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < select.Count; i++)
        {
            index[select[i]] = i;
        }

        var devices = new List<KatalogDeviceRecord>();
        var rows = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (++rows % CancelCheckEveryRows == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            devices.Add(new KatalogDeviceRecord
            {
                DeviceId = ReadDeviceInt(reader, index, "device_id"),
                ParentId = ReadDeviceInt(reader, index, "device_parent_id"),
                Name = ReadDeviceText(reader, index, "device_name"),
                Type = ReadDeviceText(reader, index, "device_type"),
                ExternalId = ReadDeviceInt(reader, index, "device_external_id"),
                Path = ReadDeviceText(reader, index, "device_path"),
                TotalFileSize = ReadDeviceLong(reader, index, "device_total_file_size"),
                TotalFileCount = ReadDeviceLong(reader, index, "device_total_file_count"),
                TotalSpace = ReadDeviceLong(reader, index, "device_total_space"),
                FreeSpace = ReadDeviceLong(reader, index, "device_free_space"),
                Active = ReadDeviceLong(reader, index, "device_active") != 0,
                GroupId = ReadDeviceInt(reader, index, "device_group_id"),
                DateUpdated = ReadDeviceText(reader, index, "device_date_updated"),
                Order = ReadDeviceInt(reader, index, "device_order"),
                Comment = ReadDeviceText(reader, index, "device_comment")
            });
        }

        return devices;
    }

    private static string ReadDeviceText(SqliteDataReader reader, Dictionary<string, int> index, string column) =>
        index.TryGetValue(column, out var ordinal) ? ReadText(reader, ordinal) : string.Empty;

    private static long ReadDeviceLong(SqliteDataReader reader, Dictionary<string, int> index, string column) =>
        index.TryGetValue(column, out var ordinal) ? ReadLong(reader, ordinal) : 0;

    private static int ReadDeviceInt(SqliteDataReader reader, Dictionary<string, int> index, string column)
    {
        var value = ReadDeviceLong(reader, index, column);
        return value > int.MaxValue ? int.MaxValue : (int)value;
    }

    private static async Task<List<KatalogStorageRecord>> ReadStoragesAsync(
        SqliteConnection connection, HashSet<string> available, CancellationToken cancellationToken)
    {
        var storages = new List<KatalogStorageRecord>();
        string? Column(string name) => available.Contains(name) ? name : null;

        var idCol = Column("storage_id");
        var userCol = Column("storage_user_id");
        var typeCol = Column("storage_type");
        var labelCol = Column("storage_label");
        var fsCol = Column("storage_file_system");
        var brandCol = Column("storage_brand");
        var modelCol = Column("storage_model");
        var serialCol = Column("storage_serial_number");
        var buildCol = Column("storage_build_date");
        var commentCols = new[] { "storage_comment1", "storage_comment2", "storage_comment3" };
        var pictureCol = Column("storage_picture_path");

        var select = new List<string>();
        if (idCol is not null) select.Add(idCol);
        if (userCol is not null) select.Add(userCol);
        if (typeCol is not null) select.Add(typeCol);
        if (labelCol is not null) select.Add(labelCol);
        if (fsCol is not null) select.Add(fsCol);
        if (brandCol is not null) select.Add(brandCol);
        if (modelCol is not null) select.Add(modelCol);
        if (serialCol is not null) select.Add(serialCol);
        if (buildCol is not null) select.Add(buildCol);
        foreach (var comment in commentCols)
        {
            if (available.Contains(comment))
            {
                select.Add(comment);
            }
        }

        if (pictureCol is not null) select.Add(pictureCol);
        if (select.Count == 0)
        {
            return storages;
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {string.Join(", ", select)} FROM storage";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (++rows % CancelCheckEveryRows == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < select.Count; i++)
            {
                values[select[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            string Get(string? column) =>
                column is not null && values.TryGetValue(column, out var value) && value is not null
                    ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
                    : string.Empty;
            int GetInt(string? column)
            {
                if (column is null || !values.TryGetValue(column, out var value) || value is null)
                {
                    return 0;
                }

                return value switch
                {
                    int number => number,
                    long number => number > int.MaxValue ? int.MaxValue : (int)number,
                    string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
                    _ => 0
                };
            }

            var comments = new List<string>();
            foreach (var comment in commentCols)
            {
                if (values.TryGetValue(comment, out var raw) && raw is not null)
                {
                    var text = Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        comments.Add(text);
                    }
                }
            }

            var label = Get(labelCol);
            storages.Add(new KatalogStorageRecord
            {
                StorageId = GetInt(idCol),
                Name = label,
                Type = Get(typeCol),
                Location = string.Empty,
                Path = string.Empty,
                Label = label,
                FileSystem = Get(fsCol),
                Total = 0,
                Free = 0,
                Brand = Get(brandCol),
                Model = Get(modelCol),
                SerialNumber = Get(serialCol),
                BuildDate = Get(buildCol),
                ContentType = string.Empty,
                Container = string.Empty,
                Comment = string.Join(" / ", comments),
                PicturePath = Get(pictureCol),
                UserId = GetInt(userCol)
            });
        }

        return storages;
    }

    private static async Task<List<KatalogTagRecord>> ReadTagsAsync(
        SqliteConnection connection, HashSet<string> available, CancellationToken cancellationToken)
    {
        var select = new List<string> { "ID", "name", "path" };
        foreach (var optional in new[] { "type", "date_time" })
        {
            if (available.Contains(optional))
            {
                select.Add(optional);
            }
        }

        var tags = new List<KatalogTagRecord>();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {string.Join(", ", select)} FROM tag";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < select.Count; i++)
        {
            index[select[i]] = i;
        }

        string Text(string column) =>
            index.TryGetValue(column, out var ordinal) ? ReadText(reader, ordinal) : string.Empty;
        var rows = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (++rows % CancelCheckEveryRows == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            tags.Add(new KatalogTagRecord
            {
                Id = ReadInt(reader, index["ID"]),
                Name = Text("name"),
                Path = Text("path"),
                Type = Text("type"),
                DateTime = Text("date_time")
            });
        }

        return tags;
    }

    private static async Task<List<KatalogIdxCatalog>> ReadCatalogsAsync(
        SqliteConnection connection,
        List<KatalogDeviceRecord> devices,
        Dictionary<string, HashSet<string>> columns,
        HashSet<string> availableFileColumns,
        CancellationToken cancellationToken)
    {
        var ownerByCatalogId = new Dictionary<int, KatalogDeviceRecord>();
        foreach (var device in devices)
        {
            if (string.Equals(device.Type, "Catalog", StringComparison.OrdinalIgnoreCase))
            {
                ownerByCatalogId.TryAdd(device.ExternalId, device);
            }
        }

        var select = new List<string> { "catalog_id", "catalog_file_path" };
        foreach (var optional in new[]
            {
                "catalog_date_updated", "catalog_include_hidden", "catalog_file_type",
                "catalog_include_symblinks", "catalog_is_full_device", "catalog_date_loaded",
                "catalog_include_metadata", "catalog_include_checksum", "catalog_app_version",
                "catalog_include_sub_dir"
            })
        {
            if (columns[KatalogSqliteSchema.CatalogTable].Contains(optional))
            {
                select.Add(optional);
            }
        }

        var catalogs = new List<KatalogIdxCatalog>();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {string.Join(", ", select)} FROM catalog ORDER BY catalog_id";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < select.Count; i++)
        {
            index[select[i]] = i;
        }

        string Text(string column) =>
            index.TryGetValue(column, out var ordinal) ? ReadText(reader, ordinal) : string.Empty;
        bool Flag(string column) =>
            index.TryGetValue(column, out var ordinal) && !reader.IsDBNull(ordinal) && IsTrueValue(reader.GetValue(ordinal));
        while (await reader.ReadAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var catalogId = ReadInt(reader, index["catalog_id"]);
            ownerByCatalogId.TryGetValue(catalogId, out var owner);

            var catalogName = owner?.Name;
            if (string.IsNullOrWhiteSpace(catalogName) && availableFileColumns.Contains("file_catalog"))
            {
                catalogName = await ReadCatalogNameFallbackAsync(connection, catalogId, cancellationToken);
            }

            catalogs.Add(new KatalogIdxCatalog
            {
                CatalogId = catalogId,
                CatalogName = string.IsNullOrWhiteSpace(catalogName) ? $"Catalog_{catalogId}" : catalogName.Trim(),
                AppVersion = Text("catalog_app_version"),
                SourcePath = owner?.Path ?? string.Empty,
                FileCount = owner?.TotalFileCount ?? 0,
                TotalFileSize = owner?.TotalFileSize ?? 0,
                IncludeHidden = Flag("catalog_include_hidden"),
                FileType = Text("catalog_file_type"),
                IncludeSymlinks = Flag("catalog_include_symblinks"),
                IncludeSubDir = Flag("catalog_include_sub_dir"),
                IsFullDevice = Flag("catalog_is_full_device"),
                IncludeMetadata = Text("catalog_include_metadata"),
                IncludeChecksum = Text("catalog_include_checksum"),
                FilePath = Text("catalog_file_path")
            });
        }

        return catalogs;
    }

    private static bool IsTrueValue(object? value) => value switch
    {
        long number => number != 0,
        int number => number != 0,
        string text => text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1",
        _ => false
    };

    private static async Task<string> ReadCatalogNameFallbackAsync(
        SqliteConnection connection, int catalogId, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT file_catalog FROM file WHERE file_catalog_id = $id LIMIT 1";
        command.Parameters.AddWithValue("$id", catalogId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static async Task ReadCatalogFilesAsync(
        SqliteConnection connection,
        KatalogIdxCatalog catalog,
        HashSet<string> available,
        CancellationToken cancellationToken)
    {
        var select = new List<string>
        {
            "file_name", "file_folder_path", "file_size", "file_date_updated", "file_full_path"
        };
        foreach (var optional in KatalogSqliteSchema.OptionalFileColumns)
        {
            if (available.Contains(optional))
            {
                select.Add(optional);
            }
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {string.Join(", ", select)} FROM file WHERE file_catalog_id = $id ORDER BY file_full_path";
        command.Parameters.AddWithValue("$id", catalog.CatalogId);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < select.Count; i++)
        {
            index[select[i]] = i;
        }

        string Text(string column) =>
            index.TryGetValue(column, out var i) ? ReadText(reader, i) : string.Empty;
        int? IntOrNull(string column)
        {
            if (!index.TryGetValue(column, out var i) || reader.IsDBNull(i))
            {
                return null;
            }

            var value = reader.GetValue(i);
            return value switch
            {
                long number => (int?)number,
                int number => number,
                string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => null
            };
        }

        double? DoubleOrNull(string column)
        {
            if (!index.TryGetValue(column, out var i) || reader.IsDBNull(i))
            {
                return null;
            }

            var value = reader.GetValue(i);
            return value switch
            {
                double number => number,
                float number => number,
                long number => number,
                int number => number,
                string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => null
            };
        }

        var rows = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (++rows % CancelCheckEveryRows == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            catalog.Files.Add(new KatalogFileEntry
            {
                FullPath = Text("file_full_path"),
                SizeBytes = ReadLong(reader, index["file_size"]),
                DateUpdated = Text("file_date_updated"),
                Extension = Text("file_extension"),
                FileType = Text("file_type"),
                MimeType = Text("mime_type"),
                MimeVerified = IntOrNull("mime_verified"),
                TypeMismatch = IntOrNull("type_mismatch"),
                ImageWidth = IntOrNull("image_width"),
                ImageHeight = IntOrNull("image_height"),
                ImageOrientation = IntOrNull("image_orientation"),
                VideoDurationSeconds = DoubleOrNull("video_duration_seconds"),
                VideoWidth = IntOrNull("video_width"),
                VideoHeight = IntOrNull("video_height"),
                VideoCodec = Text("video_codec"),
                AudioDurationSeconds = DoubleOrNull("audio_duration_seconds"),
                AudioArtist = Text("audio_artist"),
                AudioAlbum = Text("audio_album"),
                AudioTitle = Text("audio_title"),
                AudioGenre = Text("audio_genre"),
                AudioYear = IntOrNull("audio_year"),
                AudioTrackNumber = IntOrNull("audio_track_number"),
                AudioBitrate = IntOrNull("audio_bitrate"),
                AudioSampleRate = IntOrNull("audio_sample_rate"),
                MetadataExtended = Text("metadata_extended"),
                MetadataExtractionDate = Text("metadata_extraction_date"),
                ChecksumSha256 = Text("checksum_sha256"),
                ChecksumExtractionDate = Text("checksum_extraction_date")
            });
        }
    }

    private static async Task<Dictionary<string, List<string>>> ReadCatalogFoldersAsync(
        SqliteConnection connection,
        List<KatalogIdxCatalog> catalogs,
        CancellationToken cancellationToken)
    {
        var folders = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var catalog in catalogs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT DISTINCT folder_path FROM folder WHERE folder_catalog_id = $id ORDER BY folder_path";
            command.Parameters.AddWithValue("$id", catalog.CatalogId);
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var paths = new List<string>();
            while (await reader.ReadAsync(cancellationToken))
            {
                var folderPath = ReadText(reader, 0);
                if (!string.IsNullOrWhiteSpace(folderPath))
                {
                    paths.Add(folderPath);
                }
            }

            if (paths.Count > 0)
            {
                folders[catalog.CatalogName] = paths;
            }
        }

        return folders;
    }
}
