using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using FirebirdSql.Data.FirebirdClient;
using FirebirdSql.Data.Services;
using SkyCD.Plugin.Vvv.Exceptions;

namespace SkyCD.Plugin.Vvv;

/// <summary>
/// Restores a VVV logical backup into the private workspace and reads the
/// verified VVV schema: SERVICE, VOLUMES, PATHS, FILES and the optional
/// virtual-folder and audio-metadata tables.
/// </summary>
internal static class VvvBackupReader
{
    private const string SysdbaUser = "SYSDBA";

    // Embedded Firebird creates the restored database with the supplied
    // credentials; the default embedded SYSDBA password is required even
    // though the engine is local.
    private const string EmbeddedPassword = "masterkey";

    internal static VvvBackupSnapshot Read(VvvBackupWorkspace workspace, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Restore(workspace);
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = Open(workspace);
        try
        {
            var tables = ReadTableNames(connection);
            var serviceVersion = ReadSchemaVersion(connection, tables);
            EnsureRequiredTables(tables);

            var snapshot = new VvvBackupSnapshot { SchemaVersion = serviceVersion };
            var rowCount = 0;
            ReadVolumes(connection, snapshot, ref rowCount, cancellationToken);
            ReadPaths(connection, snapshot, ref rowCount, cancellationToken);
            ReadFiles(connection, snapshot, ref rowCount, cancellationToken);
            ReadAudio(connection, tables, snapshot, ref rowCount, cancellationToken);
            ReadVirtualPaths(connection, tables, snapshot, ref rowCount, cancellationToken);
            ReadVirtualFiles(connection, tables, snapshot, ref rowCount, cancellationToken);
            return snapshot;
        }
        catch (FbException exception)
        {
            throw new VvvBackupMalformedException(
                $"querying the VVV tables failed because the schema does not match a verified VVV backup. Details: {exception.Message}");
        }
    }

    private static void Restore(VvvBackupWorkspace workspace)
    {
        try
        {
            var builder = BuildConnectionString(workspace);
            var restore = new FbRestore(builder.ConnectionString)
            {
                Options = FbRestoreFlags.Create | FbRestoreFlags.NoShadow,
                ReadOnly = true,

                // Without verbose output the provider detaches as soon as the
                // restore task starts and reports success before any data is read.
                Verbose = true
            };
            restore.ServiceOutput += static (_, _) => { };
            restore.BackupFiles.Add(new FbBackupFile(workspace.BackupPath));
            restore.Execute();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new VvvBackupRestoreFailedException(exception.Message);
        }

        var database = new FileInfo(workspace.DatabasePath);
        if (!database.Exists || database.Length == 0)
        {
            throw new VvvBackupRestoreFailedException("the restored database file was not created.");
        }
    }

    private static FbConnection Open(VvvBackupWorkspace workspace)
    {
        try
        {
            var builder = BuildConnectionString(workspace);
            builder.NoDatabaseTriggers = true;
            var connection = new FbConnection(builder.ConnectionString);
            connection.Open();
            return connection;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new VvvBackupRestoreFailedException(exception.Message);
        }
    }

    private static FbConnectionStringBuilder BuildConnectionString(VvvBackupWorkspace workspace) =>
        new()
        {
            Database = workspace.DatabasePath,
            UserID = SysdbaUser,
            Password = EmbeddedPassword,
            ServerType = FbServerType.Embedded,
            ClientLibrary = workspace.ClientLibraryPath,
            Charset = "UTF8",
            Pooling = false
        };

    private static HashSet<string> ReadTableNames(FbConnection connection)
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT TRIM(RDB$RELATION_NAME) FROM RDB$RELATIONS WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
            {
                tables.Add(reader.GetString(0));
            }
        }

        return tables;
    }

    private static int ReadSchemaVersion(FbConnection connection, HashSet<string> tables)
    {
        if (!tables.Contains("SERVICE"))
        {
            throw new VvvBackupMissingSchemaVersionException();
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DB_VERSION FROM SERVICE WHERE SERVICE_ID = 1";
        var value = command.ExecuteScalar();
        if (value is null or DBNull)
        {
            throw new VvvBackupMissingSchemaVersionException();
        }

        var version = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        if (version < VvvBackupLimits.MinSupportedSchemaVersion ||
            version > VvvBackupLimits.MaxSupportedSchemaVersion)
        {
            throw new VvvBackupUnsupportedSchemaException(version);
        }

        return version;
    }

    private static void EnsureRequiredTables(HashSet<string> tables)
    {
        foreach (var required in new[] { "VOLUMES", "PATHS", "FILES" })
        {
            if (!tables.Contains(required))
            {
                throw new VvvBackupMalformedException($"required table {required} is missing");
            }
        }
    }

    private static void ReadVolumes(
        FbConnection connection,
        VvvBackupSnapshot snapshot,
        ref int rowCount,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT VOLUME_ID, VOLUME_NAME, VOLUME_DESCRIPTION, PHYSICAL_PATH, CATALOG_DATE, LAST_UPDATE_DATE FROM VOLUMES ORDER BY VOLUME_ID";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            CountRow(ref rowCount);
            cancellationToken.ThrowIfCancellationRequested();
            snapshot.Volumes.Add(new VvvBackupVolume(
                reader.GetInt64(0),
                Text(reader, 1),
                Text(reader, 2),
                Text(reader, 3),
                Timestamp(reader, 4),
                Timestamp(reader, 5)));
        }
    }

    private static void ReadPaths(
        FbConnection connection,
        VvvBackupSnapshot snapshot,
        ref int rowCount,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT PATH_ID, VOLUME_ID, PATH_NAME, FATHER_ID, PATH_DESCRIPTION FROM PATHS ORDER BY PATH_ID";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            CountRow(ref rowCount);
            cancellationToken.ThrowIfCancellationRequested();
            snapshot.Paths.Add(new VvvBackupPath(
                reader.GetInt64(0),
                reader.GetInt64(1),
                Text(reader, 2),
                NullableInt64(reader, 3),
                Text(reader, 4)));
        }
    }

    private static void ReadFiles(
        FbConnection connection,
        VvvBackupSnapshot snapshot,
        ref int rowCount,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT FILE_ID, FILE_NAME, FILE_SIZE, FILE_EXT, FILE_DATETIME, PATH_ID, PATH_FILE_ID, FILE_DESCRIPTION FROM FILES ORDER BY FILE_ID";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            CountRow(ref rowCount);
            cancellationToken.ThrowIfCancellationRequested();
            snapshot.Files.Add(new VvvBackupFile(
                reader.GetInt64(0),
                Text(reader, 1),
                reader.IsDBNull(2) ? 0L : reader.GetInt64(2),
                Text(reader, 3),
                Timestamp(reader, 4),
                NullableInt64(reader, 5),
                NullableInt64(reader, 6),
                Text(reader, 7)));
        }
    }

    private static void ReadAudio(
        FbConnection connection,
        HashSet<string> tables,
        VvvBackupSnapshot snapshot,
        ref int rowCount,
        CancellationToken cancellationToken)
    {
        if (!tables.Contains("FILES_AUDIO_METADATA"))
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT FILE_ID, TRACK_TITLE, TRACK_ARTIST, TRACK_ALBUM, TRACK_YEAR, TRACK_COMMENT, TRACK_NUMBER, TRACK_GENRE, TRACK_BITRATE, TRACK_SAMPLE_RATE, TRACK_CHANNELS, TRACK_LENGTH FROM FILES_AUDIO_METADATA";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            CountRow(ref rowCount);
            cancellationToken.ThrowIfCancellationRequested();
            snapshot.AudioByFileId[reader.GetInt64(0)] = new VvvBackupAudio(
                Text(reader, 1),
                Text(reader, 2),
                Text(reader, 3),
                NullableInt32(reader, 4),
                Text(reader, 5),
                NullableInt32(reader, 6),
                Text(reader, 7),
                NullableInt32(reader, 8),
                NullableInt32(reader, 9),
                NullableInt16(reader, 10),
                NullableInt32(reader, 11));
        }
    }

    private static void ReadVirtualPaths(
        FbConnection connection,
        HashSet<string> tables,
        VvvBackupSnapshot snapshot,
        ref int rowCount,
        CancellationToken cancellationToken)
    {
        if (!tables.Contains("VIRTUAL_PATHS"))
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT PATH_ID, PATH, FATHER_ID, PHYS_PATH_ID FROM VIRTUAL_PATHS ORDER BY PATH_ID";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            CountRow(ref rowCount);
            cancellationToken.ThrowIfCancellationRequested();
            snapshot.VirtualPaths.Add(new VvvBackupVirtualPath(
                reader.GetInt64(0),
                Text(reader, 1),
                NullableInt64(reader, 2),
                NullableInt64(reader, 3)));
        }
    }

    private static void ReadVirtualFiles(
        FbConnection connection,
        HashSet<string> tables,
        VvvBackupSnapshot snapshot,
        ref int rowCount,
        CancellationToken cancellationToken)
    {
        if (!tables.Contains("VIRTUAL_FILES"))
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT FILE_ID, VIRTUAL_PATH_ID, PHYSICAL_FILE_ID, VIRTUAL_PATH_FILE_ID FROM VIRTUAL_FILES";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            CountRow(ref rowCount);
            cancellationToken.ThrowIfCancellationRequested();
            snapshot.VirtualFiles.Add(new VvvBackupVirtualFile(
                reader.GetInt64(0),
                reader.GetInt64(1),
                NullableInt64(reader, 2),
                NullableInt64(reader, 3)));
        }
    }

    private static void CountRow(ref int rowCount)
    {
        rowCount++;
        if (rowCount > VvvBackupLimits.MaxDatabaseRows)
        {
            throw new VvvBackupLimitExceededException("the backup contains more than 2,000,000 catalog rows");
        }
    }

    private static string? Text(FbDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : reader.GetString(index);

    private static DateTime? Timestamp(FbDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : reader.GetDateTime(index);

    private static long? NullableInt64(FbDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : reader.GetInt64(index);

    private static int? NullableInt32(FbDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : Convert.ToInt32(reader.GetValue(index), CultureInfo.InvariantCulture);

    private static short? NullableInt16(FbDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : Convert.ToInt16(reader.GetValue(index), CultureInfo.InvariantCulture);
}
