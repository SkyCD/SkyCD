using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace SkyCD.Plugin.Katalog.Sqlite.Tests;

/// <summary>
/// Builds synthetic Katalog File-mode SQLite databases. The schema mirrors the
/// upstream Katalog table factories (core/database.cpp); data is synthetic
/// and exists only to exercise the import contract.
/// </summary>
public sealed class KatalogFixtureBuilder : IDisposable
{
    private readonly string directory;
    private readonly List<string> files = [];

    public KatalogFixtureBuilder()
    {
        directory = Path.Combine(Path.GetTempPath(), $"skycd-katalog-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
    }

    public string DirectoryPath => directory;

    public void Dispose()
    {
        foreach (var file in files)
        {
            DeleteQuietly(file);
            DeleteQuietly(file + "-wal");
            DeleteQuietly(file + "-shm");
            DeleteQuietly(file + "-journal");
        }

        try { Directory.Delete(directory, recursive: true); } catch { }
    }

    private static void DeleteQuietly(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    public string CreateFullCollection(string fileName = "collection.db", string version = "2.10")
    {
        var path = Path.Combine(directory, fileName);
        files.Add(path);
        using var connection = new SqliteConnection($"Data Source={path};Pooling=false");
        connection.Open();

        Execute(connection, "PRAGMA journal_mode = WAL");
        Execute(connection, @"CREATE TABLE device(
            device_id NUMERIC PRIMARY KEY, device_parent_id NUMERIC, device_name TEXT,
            device_type TEXT, device_external_id NUMERIC, device_path TEXT,
            device_total_file_size NUMERIC default 0, device_total_file_count NUMERIC default 0,
            device_total_space NUMERIC default 0, device_free_space NUMERIC default 0,
            device_active NUMERIC, device_group_id NUMERIC, device_date_updated TEXT,
            device_order NUMERIC, device_comment TEXT)");
        Execute(connection, @"CREATE TABLE catalog(
            catalog_id NUMERIC, catalog_file_path TEXT, catalog_date_updated TEXT,
            catalog_include_hidden TEXT, catalog_file_type TEXT, catalog_include_symblinks TEXT,
            catalog_is_full_device TEXT, catalog_date_loaded TEXT, catalog_include_metadata TEXT,
            catalog_include_checksum TEXT, catalog_app_version TEXT, catalog_include_sub_dir TEXT,
            PRIMARY KEY(catalog_id))");
        Execute(connection, @"CREATE TABLE storage(
            storage_id NUMERIC primary key default 0, storage_user_id NUMERIC default 0,
            storage_type TEXT, storage_label TEXT, storage_file_system TEXT, storage_brand TEXT,
            storage_model TEXT, storage_serial_number TEXT, storage_build_date TEXT,
            storage_comment1 TEXT, storage_comment2 TEXT, storage_comment3 TEXT, storage_picture_path TEXT)");
        Execute(connection, @"CREATE TABLE file(
            file_catalog_id NUMERIC, file_name TEXT, file_folder_path TEXT, file_size NUMERIC,
            file_date_updated TEXT, file_catalog TEXT, file_full_path TEXT, file_extension TEXT,
            file_type TEXT, mime_type TEXT, mime_verified NUMERIC, type_mismatch NUMERIC,
            image_width NUMERIC, image_height NUMERIC, image_orientation NUMERIC,
            video_duration_seconds NUMERIC, video_width NUMERIC, video_height NUMERIC,
            video_codec TEXT, video_framerate NUMERIC, video_bitrate NUMERIC,
            audio_duration_seconds NUMERIC, audio_artist TEXT, audio_album TEXT, audio_title TEXT,
            audio_genre TEXT, audio_year NUMERIC, audio_track_number NUMERIC, audio_bitrate NUMERIC,
            audio_sample_rate NUMERIC, metadata_extended TEXT, metadata_extraction_date TEXT,
            checksum_sha256 TEXT, checksum_extraction_date TEXT)");
        Execute(connection, @"CREATE TABLE folder(
            folder_catalog_id NUMERIC, folder_path TEXT, PRIMARY KEY(folder_catalog_id, folder_path))");
        Execute(connection, @"CREATE TABLE tag(
            ID NUMERIC, name TEXT, path TEXT, type TEXT, date_time TEXT)");
        Execute(connection, @"CREATE TABLE parameter(
            parameter_name TEXT, parameter_type TEXT, parameter_value1 TEXT, parameter_value2 TEXT)");

        Execute(connection, $"INSERT INTO parameter (parameter_name, parameter_type, parameter_value1) VALUES ('version', 'collection', '{version}')");

        // Group device + catalog device (external_id joins catalog 10)
        Execute(connection, "INSERT INTO device VALUES (1, 0, 'Archive Group', 'Group', 0, '', 0, 0, 0, 0, 1, 0, '2024-01-01', 0, '')");
        Execute(connection, "INSERT INTO device VALUES (2, 1, 'PhotoDVD', 'Catalog', 10, '/mnt/photos', 2048, 2, 4700000000, 1000, 1, 0, '2024-02-01', 0, 'comment')");
        Execute(connection, "INSERT INTO storage VALUES (5, 0, 'DVD', 'PHOTOS_2024', 'iso9660', 'Brand', 'Model', 'SN123', '2023-05-01', 'c1', '', '', '')");
        Execute(connection, "INSERT INTO device VALUES (3, 0, 'MusicStick', 'Storage', 5, '/mnt/music', 512, 1, 8000000000, 2000, 1, 0, '2024-03-01', 0, '')");

        // Catalog 10 owned by device 2; catalog 11 standalone (no device row)
        Execute(connection, "INSERT INTO catalog VALUES (10, '/backups/photos.idx', '2024-02-01', 'false', 'Photos', 'false', 'true', '2024-02-02', 'All', 'SHA256', '2.10', 'true')");
        Execute(connection, "INSERT INTO catalog VALUES (11, '/backups/music.idx', '2024-03-01', 'true', 'Audio', 'false', 'false', '2024-03-02', 'None', 'None', '2.10', 'true')");

        Execute(connection, "INSERT INTO folder VALUES (10, '/photos/2024')");
        InsertFile(connection, new Dictionary<string, object?>
        {
            ["file_catalog_id"] = 10,
            ["file_name"] = "img.jpg",
            ["file_folder_path"] = "/photos/2024",
            ["file_size"] = 1024,
            ["file_date_updated"] = "2024-02-01",
            ["file_catalog"] = "PhotoDVD",
            ["file_full_path"] = "/photos/2024/img.jpg",
            ["file_extension"] = "jpg",
            ["file_type"] = "Image",
            ["mime_type"] = "image/jpeg",
            ["mime_verified"] = 1,
            ["type_mismatch"] = 0,
            ["image_width"] = 800,
            ["image_height"] = 600,
            ["image_orientation"] = 1,
            ["metadata_extended"] = "{\"title\":\"Sunset\"}",
            ["metadata_extraction_date"] = "2024-02-02",
            ["checksum_sha256"] = "abc123",
            ["checksum_extraction_date"] = "2024-02-02"
        });
        InsertFile(connection, new Dictionary<string, object?>
        {
            ["file_catalog_id"] = 10,
            ["file_name"] = "notes.txt",
            ["file_folder_path"] = "/photos/2024",
            ["file_size"] = 1024,
            ["file_date_updated"] = "2024-02-01",
            ["file_catalog"] = "PhotoDVD",
            ["file_full_path"] = "/photos/2024/notes.txt",
            ["file_extension"] = "txt",
            ["file_type"] = "Text",
            ["mime_type"] = "text/plain"
        });
        InsertFile(connection, new Dictionary<string, object?>
        {
            ["file_catalog_id"] = 11,
            ["file_name"] = "song.mp3",
            ["file_folder_path"] = "/music",
            ["file_size"] = 512,
            ["file_date_updated"] = "2024-03-01",
            ["file_catalog"] = "MusicFiles",
            ["file_full_path"] = "/music/song.mp3",
            ["file_extension"] = "mp3",
            ["file_type"] = "Audio",
            ["mime_type"] = "audio/mpeg",
            ["audio_duration_seconds"] = 180.5,
            ["audio_artist"] = "Artist",
            ["audio_album"] = "Album",
            ["audio_title"] = "Song",
            ["audio_genre"] = "Rock",
            ["audio_year"] = 2001,
            ["audio_track_number"] = 3,
            ["audio_bitrate"] = 192,
            ["audio_sample_rate"] = 44100,
            ["checksum_sha256"] = "def456"
        });

        Execute(connection, "INSERT INTO tag VALUES (1, 'vacation', '/photos/2024/img.jpg', 'file', '2024-02-03')");
        Execute(connection, "INSERT INTO tag VALUES (2, 'rock', '/music/song.mp3', 'file', '2024-03-03')");

        // Leave WAL content un-checkpointed so the -wal sidecar exists for snapshot tests.
        Execute(connection, "PRAGMA wal_checkpoint(PASSIVE)");
        return path;
    }

    public string CreateMinimalCollection(string fileName = "minimal.db", string version = "2.8")
    {
        var path = Path.Combine(directory, fileName);
        files.Add(path);
        using var connection = new SqliteConnection($"Data Source={path};Pooling=false");
        connection.Open();

        Execute(connection, "CREATE TABLE device(device_id NUMERIC PRIMARY KEY, device_parent_id NUMERIC, device_name TEXT, device_type TEXT, device_external_id NUMERIC, device_path TEXT)");
        Execute(connection, "CREATE TABLE catalog(catalog_id NUMERIC, catalog_file_path TEXT, PRIMARY KEY(catalog_id))");
        Execute(connection, @"CREATE TABLE file(file_catalog_id NUMERIC, file_name TEXT, file_folder_path TEXT,
            file_size NUMERIC, file_date_updated TEXT, file_full_path TEXT)");
        Execute(connection, "CREATE TABLE folder(folder_catalog_id NUMERIC, folder_path TEXT, PRIMARY KEY(folder_catalog_id, folder_path))");
        Execute(connection, "CREATE TABLE tag(ID NUMERIC, name TEXT, path TEXT)");
        Execute(connection, "CREATE TABLE parameter(parameter_name TEXT, parameter_type TEXT, parameter_value1 TEXT)");

        Execute(connection, $"INSERT INTO parameter (parameter_name, parameter_type, parameter_value1) VALUES ('version', 'collection', '{version}')");
        Execute(connection, "INSERT INTO device VALUES (1, 0, 'Docs', 'Catalog', 7, '/docs')");
        Execute(connection, "INSERT INTO catalog VALUES (7, '/backups/docs.idx')");
        Execute(connection, "INSERT INTO file VALUES (7, 'readme.txt', '/', 42, '2024-01-01', '/readme.txt')");
        return path;
    }

    public string CreateEmptyCatalogs(string fileName = "empty.db")
    {
        var path = Path.Combine(directory, fileName);
        files.Add(path);
        using var connection = new SqliteConnection($"Data Source={path};Pooling=false");
        connection.Open();

        Execute(connection, "CREATE TABLE device(device_id NUMERIC PRIMARY KEY, device_parent_id NUMERIC, device_name TEXT, device_type TEXT, device_external_id NUMERIC, device_path TEXT)");
        Execute(connection, "CREATE TABLE catalog(catalog_id NUMERIC, catalog_file_path TEXT, PRIMARY KEY(catalog_id))");
        Execute(connection, @"CREATE TABLE file(file_catalog_id NUMERIC, file_name TEXT, file_folder_path TEXT,
            file_size NUMERIC, file_date_updated TEXT, file_full_path TEXT)");
        Execute(connection, "CREATE TABLE folder(folder_catalog_id NUMERIC, folder_path TEXT)");
        Execute(connection, "CREATE TABLE tag(ID NUMERIC, name TEXT, path TEXT)");
        Execute(connection, "CREATE TABLE parameter(parameter_name TEXT, parameter_type TEXT, parameter_value1 TEXT)");
        Execute(connection, "INSERT INTO parameter (parameter_name, parameter_type, parameter_value1) VALUES ('version', 'collection', '2.10')");
        Execute(connection, "INSERT INTO device VALUES (1, 0, 'Lonely', 'Group', 0, '')");
        return path;
    }

    public string CreateNonKatalogDatabase(string fileName = "other.db")
    {
        var path = Path.Combine(directory, fileName);
        files.Add(path);
        using var connection = new SqliteConnection($"Data Source={path};Pooling=false");
        connection.Open();

        Execute(connection, "CREATE TABLE notes(id INTEGER PRIMARY KEY, body TEXT)");
        Execute(connection, "INSERT INTO notes VALUES (1, 'hello')");
        return path;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void InsertFile(SqliteConnection connection, Dictionary<string, object?> values)
    {
        var columns = new List<string>(values.Keys);
        using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO file ({string.Join(", ", columns)}) VALUES ({string.Join(", ", columns.ConvertAll(c => "$" + c))})";
        foreach (var column in columns)
        {
            command.Parameters.AddWithValue("$" + column, values[column] ?? DBNull.Value);
        }

        command.ExecuteNonQuery();
    }
}



