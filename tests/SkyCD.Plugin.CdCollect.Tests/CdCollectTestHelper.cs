using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace SkyCD.Plugin.CdCollect.Tests;

public static class CdCollectTestHelper
{
    public static void CreateSampleV4Database(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        using (var conn = new SqliteConnection($"Data Source={filePath};Pooling=False"))
        {
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
            CREATE TABLE cdcollect_version (ver INTEGER);
            INSERT INTO cdcollect_version (ver) VALUES (4);

            CREATE TABLE cds (
                id INTEGER PRIMARY KEY NOT NULL,
                name TEXT,
                keywords TEXT,
                year INTEGER,
                month INTEGER,
                day INTEGER,
                icon TEXT
            );

            CREATE TABLE files_v1 (
                cd_id INTEGER,
                path TEXT,
                name TEXT,
                info TEXT,
                size INTEGER,
                mime TEXT,
                location TEXT,
                type INTEGER,
                packed INTEGER
            );

            CREATE TABLE infoplugins (
                mime TEXT UNIQUE,
                program TEXT,
                arguments TEXT,
                regex_input TEXT,
                regex_pattern TEXT,
                regex_case INTEGER
            );

            CREATE TABLE compplugins (
                mime TEXT UNIQUE,
                program TEXT,
                arguments TEXT
            );

            INSERT INTO compplugins (mime, program, arguments) VALUES ('application/zip', '/usr/bin/unzip', '{0} -d {1}');
            INSERT INTO compplugins (mime, program, arguments) VALUES ('application/x-tar', '/bin/tar', 'xf {0} -C {1}');

            INSERT INTO cds (id, name, keywords, year, month, day, icon)
            VALUES (1, 'Rock Albums 2024', 'rock, classic, flac', 2024, 5, 10, 'cdrom');

            INSERT INTO cds (id, name, keywords, year, month, day, icon)
            VALUES (2, 'Backup Drive', 'documents, archives', 2024, 6, 1, 'harddisk');

            -- CD 1 files
            -- Directory Music (node 0)
            INSERT INTO files_v1 (cd_id, path, name, info, size, mime, location, type, packed)
            VALUES (1, '0', 'Music', 'Audio collection', 0, 'inode/directory', '/Music', 0, 0);

            -- Subdirectory Rock (node 0:0)
            INSERT INTO files_v1 (cd_id, path, name, info, size, mime, location, type, packed)
            VALUES (1, '0:0', 'Rock', '', 0, 'inode/directory', '/Music/Rock', 0, 0);

            -- File track01.mp3 (node 0:0:0)
            INSERT INTO files_v1 (cd_id, path, name, info, size, mime, location, type, packed)
            VALUES (1, '0:0:0', 'track01.mp3', 'Artist: Queen\nAlbum: A Night at the Opera\nTitle: Bohemian Rhapsody\nBitrate: 320 kbps\nDuration: 5:55', 8400000, 'audio/mpeg', '/Music/Rock', 1, 0);

            -- File track02.flac (node 0:0:1)
            INSERT INTO files_v1 (cd_id, path, name, info, size, mime, location, type, packed)
            VALUES (1, '0:0:1', 'track02.flac', 'Artist: Pink Floyd\nAlbum: The Wall\nTitle: Comfortably Numb\nBitrate: 900 kbps\nDuration: 6:22', 25000000, 'audio/flac', '/Music/Rock', 1, 0);

            -- CD 2 files
            -- File report.pdf (node 0)
            INSERT INTO files_v1 (cd_id, path, name, info, size, mime, location, type, packed)
            VALUES (2, '0', 'report.pdf', 'Author: Admin\nPages: 42', 1500000, 'application/pdf', '/', 1, 0);

            -- File archive.zip (node 1)
            INSERT INTO files_v1 (cd_id, path, name, info, size, mime, location, type, packed)
            VALUES (2, '1', 'archive.zip', 'Contains compressed data', 5000000, 'application/zip', '/', 1, 0);
            """;
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
    }

    public static void CreateLegacyV0Database(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        using (var conn = new SqliteConnection($"Data Source={filePath};Pooling=False"))
        {
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
            CREATE TABLE cds (
                id INTEGER PRIMARY KEY NOT NULL,
                name TEXT,
                keywords TEXT,
                year INTEGER,
                month INTEGER,
                day INTEGER,
                icon TEXT
            );

            CREATE TABLE files (
                cd_id INTEGER,
                path TEXT,
                name TEXT,
                info TEXT,
                size INTEGER,
                mime TEXT,
                type INTEGER,
                packed INTEGER
            );

            INSERT INTO cds (id, name, keywords, year, month, day, icon)
            VALUES (1, 'Legacy Disc', 'old catalog', 2005, 3, 15, '');

            INSERT INTO files (cd_id, path, name, info, size, mime, type, packed)
            VALUES (1, '0', 'Documents', '', 0, 'inode/directory', 0, 0);

            INSERT INTO files (cd_id, path, name, info, size, mime, type, packed)
            VALUES (1, '0:0', 'old_notes.txt', 'Comments: Important notes', 1024, 'text/plain', 1, 0);
            """;
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
    }

    public static void CreateUnicodeDatabase(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        using (var conn = new SqliteConnection($"Data Source={filePath};Pooling=False"))
        {
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
            CREATE TABLE cdcollect_version (ver INTEGER);
            INSERT INTO cdcollect_version (ver) VALUES (4);

            CREATE TABLE cds (
                id INTEGER PRIMARY KEY NOT NULL,
                name TEXT,
                keywords TEXT,
                year INTEGER,
                month INTEGER,
                day INTEGER,
                icon TEXT
            );

            CREATE TABLE files_v1 (
                cd_id INTEGER,
                path TEXT,
                name TEXT,
                info TEXT,
                size INTEGER,
                mime TEXT,
                location TEXT,
                type INTEGER,
                packed INTEGER
            );

            INSERT INTO cds (id, name, keywords, year, month, day, icon)
            VALUES (1, 'コレクション 2024 (Collection)', '日本語, ロック', 2024, 7, 1, 'cd');

            INSERT INTO files_v1 (cd_id, path, name, info, size, mime, location, type, packed)
            VALUES (1, '0', 'アルバム (Album)', '', 0, 'inode/directory', '/アルバム', 0, 0);

            INSERT INTO files_v1 (cd_id, path, name, info, size, mime, location, type, packed)
            VALUES (1, '0:0', '夏祭り.mp3', 'Artist: 初音ミク\nTitle: 夏祭り\nAlbum: ボーカロイド', 4500000, 'audio/mpeg', '/アルバム', 1, 0);

            INSERT INTO files_v1 (cd_id, path, name, info, size, mime, location, type, packed)
            VALUES (1, '1', 'Отчёт_2024.pdf', 'Годовой отчёт', 2200000, 'application/pdf', '/', 1, 0);

            INSERT INTO files_v1 (cd_id, path, name, info, size, mime, location, type, packed)
            VALUES (1, '2', 'Café_Paris.jpg', 'Dimensions: 1920x1080', 1200000, 'image/jpeg', '/', 1, 0);
            """;
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
    }

    public static void CreateEmptyDatabase(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        using (var conn = new SqliteConnection($"Data Source={filePath};Pooling=False"))
        {
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
            CREATE TABLE cdcollect_version (ver INTEGER);
            INSERT INTO cdcollect_version (ver) VALUES (4);

            CREATE TABLE cds (
                id INTEGER PRIMARY KEY NOT NULL,
                name TEXT,
                keywords TEXT,
                year INTEGER,
                month INTEGER,
                day INTEGER,
                icon TEXT
            );

            CREATE TABLE files_v1 (
                cd_id INTEGER,
                path TEXT,
                name TEXT,
                info TEXT,
                size INTEGER,
                mime TEXT,
                location TEXT,
                type INTEGER,
                packed INTEGER
            );
            """;
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
    }

    public static void CreateNonCdCollectDatabase(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        using (var conn = new SqliteConnection($"Data Source={filePath};Pooling=False"))
        {
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
            CREATE TABLE users (id INTEGER PRIMARY KEY, username TEXT);
            INSERT INTO users (username) VALUES ('alice');
            """;
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
    }

    public static void EnsureFixtureFilesExist(string fixtureDir)
    {
        Directory.CreateDirectory(fixtureDir);

        var sampleV4Path = Path.Combine(fixtureDir, "cdcollect-sample-v4.db");
        if (!File.Exists(sampleV4Path))
        {
            CreateSampleV4Database(sampleV4Path);
        }

        var legacyV0Path = Path.Combine(fixtureDir, "cdcollect-legacy-v0.db");
        if (!File.Exists(legacyV0Path))
        {
            CreateLegacyV0Database(legacyV0Path);
        }

        var unicodePath = Path.Combine(fixtureDir, "cdcollect-unicode.db");
        if (!File.Exists(unicodePath))
        {
            CreateUnicodeDatabase(unicodePath);
        }

        var emptyPath = Path.Combine(fixtureDir, "cdcollect-empty.db");
        if (!File.Exists(emptyPath))
        {
            CreateEmptyDatabase(emptyPath);
        }

        var nonCdCollectPath = Path.Combine(fixtureDir, "non-cdcollect.db");
        if (!File.Exists(nonCdCollectPath))
        {
            CreateNonCdCollectDatabase(nonCdCollectPath);
        }
    }
}
