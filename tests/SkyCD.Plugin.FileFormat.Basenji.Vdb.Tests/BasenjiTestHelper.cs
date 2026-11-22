using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace SkyCD.Plugin.FileFormat.Basenji.Vdb.Tests;

public static class BasenjiTestHelper
{
    public static byte[] CreateSampleVdbBytes()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"sample-{Guid.NewGuid():N}.vdb");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={tempFile};Pooling=false;"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE DatabaseProperties (
                        Name TEXT,
                        Description TEXT,
                        Created TEXT,
                        Version INTEGER,
                        GUID TEXT
                    );

                    INSERT INTO DatabaseProperties VALUES (
                        'My Basenji Collection',
                        'Main media backup collection',
                        '2024-05-10 12:00:00',
                        4,
                        'guid-1234-5678'
                    );

                    CREATE TABLE Volumes (
                        VolumeID INTEGER PRIMARY KEY,
                        Title TEXT,
                        Added TEXT,
                        VolumeType INTEGER,
                        IsHashed INTEGER,
                        ArchiveNr TEXT,
                        DriveType TEXT,
                        Loaned_To TEXT,
                        Loaned_Date TEXT,
                        Return_Date TEXT,
                        Category TEXT,
                        Description TEXT,
                        Keywords TEXT,
                        Files INTEGER,
                        Dirs INTEGER,
                        Size INTEGER
                    );

                    INSERT INTO Volumes VALUES (
                        1,
                        'Backup CD 1',
                        '2024-05-10 12:00:00',
                        0,
                        1,
                        'ARC-001',
                        'CDRom',
                        'Alice',
                        '2024-05-11',
                        '2024-06-01',
                        'Software',
                        'Software tools and music',
                        'tools, backup',
                        2,
                        1,
                        10485760
                    );

                    INSERT INTO Volumes VALUES (
                        2,
                        'Greatest Hits CD',
                        '2024-05-12 14:00:00',
                        1,
                        0,
                        'ARC-002',
                        'CDRom',
                        '',
                        '',
                        '',
                        'Music',
                        'Audio album',
                        'rock, audio',
                        2,
                        0,
                        3600
                    );

                    CREATE TABLE Items (
                        VolumeID INTEGER,
                        ItemID INTEGER,
                        ParentID INTEGER,
                        ItemType INTEGER,
                        Name TEXT,
                        MimeType TEXT,
                        MetaData TEXT,
                        Note TEXT,
                        Keywords TEXT,
                        Hash TEXT,
                        IsContainer INTEGER,
                        Location TEXT,
                        LastWriteTime TEXT,
                        SymLinkTargetID INTEGER,
                        Size INTEGER,
                        PRIMARY KEY (VolumeID, ItemID)
                    );

                    -- Root item for Volume 1
                    INSERT INTO Items VALUES (
                        1, 1, -1, 0, '', '', '', 'Root note', '', '', 1, '', '2024-05-10 12:00:00', -1, 0
                    );

                    -- Folder under Volume 1
                    INSERT INTO Items VALUES (
                        1, 2, 1, 0, 'Docs', '', '', 'Folder note', 'documents', '', 1, 'Docs', '2024-05-10 12:00:00', -1, 0
                    );

                    -- File under Folder 2
                    INSERT INTO Items VALUES (
                        1, 3, 2, 1, 'report.pdf', 'application/pdf', '', 'Annual report', 'pdf, report', 'hash-md5-1234', 0, 'Docs/report.pdf', '2024-05-10 12:00:00', -1, 5242880
                    );

                    -- MP3 File with metadata under Volume 1
                    INSERT INTO Items VALUES (
                        1, 4, 1, 1, 'song.mp3', 'audio/mpeg', '[5:8:11:9:3:8:115:4]The BandBest HitsSong One2024', 'Favorite song', 'rock', 'hash-song-5678', 0, 'song.mp3', '2024-05-10 12:00:00', -1, 5242880
                    );

                    -- Root item for Volume 2 (Audio CD)
                    INSERT INTO Items VALUES (
                        2, 1, -1, 0, '', '', '', '', '', '', 1, '', '2024-05-12 14:00:00', -1, 0
                    );

                    -- Track 1 for Volume 2
                    INSERT INTO Items VALUES (
                        2, 2, 1, 4, 'Track 01', 'audio/x-cda', '[5:8:3:7]The BandTrack 1', 'Intro track', 'cda', '', 0, 'Track01', '2024-05-12 14:00:00', -1, 1800
                    );

                    -- Track 2 for Volume 2
                    INSERT INTO Items VALUES (
                        2, 3, 1, 4, 'Track 02', 'audio/x-cda', '[5:8:3:7]The BandTrack 2', 'Outro track', 'cda', '', 0, 'Track02', '2024-05-12 14:00:00', -1, 1800
                    );
                    """;
                command.ExecuteNonQuery();
            }

            return File.ReadAllBytes(tempFile);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    public static byte[] CreateUnicodeVdbBytes()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"unicode-{Guid.NewGuid():N}.vdb");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={tempFile};Pooling=false;"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE DatabaseProperties (
                        Name TEXT,
                        Description TEXT,
                        Created TEXT,
                        Version INTEGER,
                        GUID TEXT
                    );

                    INSERT INTO DatabaseProperties VALUES (
                        'メディア コレクション (Media Collection)',
                        'Описание архива',
                        '2024-07-01 10:00:00',
                        4,
                        'guid-unicode-001'
                    );

                    CREATE TABLE Volumes (
                        VolumeID INTEGER PRIMARY KEY,
                        Title TEXT,
                        Added TEXT,
                        VolumeType INTEGER,
                        IsHashed INTEGER,
                        ArchiveNr TEXT,
                        DriveType TEXT,
                        Loaned_To TEXT,
                        Loaned_Date TEXT,
                        Return_Date TEXT,
                        Category TEXT,
                        Description TEXT,
                        Keywords TEXT,
                        Files INTEGER,
                        Dirs INTEGER,
                        Size INTEGER
                    );

                    INSERT INTO Volumes VALUES (
                        1,
                        'メディア １ (Media 1)',
                        '2024-07-01 10:00:00',
                        0,
                        0,
                        'ARC-JP-01',
                        'Fixed',
                        '太郎 (Taro)',
                        '2024-07-02',
                        '2024-08-01',
                        '写真 (Photos)',
                        'アルバム (Photo album)',
                        'été, vacances',
                        2,
                        1,
                        1024
                    );

                    CREATE TABLE Items (
                        VolumeID INTEGER,
                        ItemID INTEGER,
                        ParentID INTEGER,
                        ItemType INTEGER,
                        Name TEXT,
                        MimeType TEXT,
                        MetaData TEXT,
                        Note TEXT,
                        Keywords TEXT,
                        Hash TEXT,
                        IsContainer INTEGER,
                        Location TEXT,
                        LastWriteTime TEXT,
                        SymLinkTargetID INTEGER,
                        Size INTEGER,
                        PRIMARY KEY (VolumeID, ItemID)
                    );

                    INSERT INTO Items VALUES (
                        1, 1, -1, 0, '', '', '', '', '', '', 1, '', '2024-07-01 10:00:00', -1, 0
                    );

                    INSERT INTO Items VALUES (
                        1, 2, 1, 0, 'ドキュメント (Documents)', '', '', 'Папка с документами', '', '', 1, 'Docs', '2024-07-01 10:00:00', -1, 0
                    );

                    INSERT INTO Items VALUES (
                        1, 3, 2, 1, 'отчёт_финал.txt', 'text/plain', '', 'Финальный отчет', '', '', 0, 'Docs/отчёт_финал.txt', '2024-07-01 10:00:00', -1, 512
                    );

                    INSERT INTO Items VALUES (
                        1, 4, 1, 1, 'café_été.jpg', 'image/jpeg', '', 'Photo de vacances', '', '', 0, 'café_été.jpg', '2024-07-01 10:00:00', -1, 512
                    );
                    """;
                command.ExecuteNonQuery();
            }

            return File.ReadAllBytes(tempFile);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    public static byte[] CreateEmptyCatalogVdbBytes()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"empty-{Guid.NewGuid():N}.vdb");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={tempFile};Pooling=false;"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE DatabaseProperties (
                        Name TEXT,
                        Description TEXT,
                        Created TEXT,
                        Version INTEGER,
                        GUID TEXT
                    );

                    INSERT INTO DatabaseProperties VALUES (
                        'Empty Collection',
                        'No volumes',
                        '2024-05-10 12:00:00',
                        4,
                        'guid-empty'
                    );

                    CREATE TABLE Volumes (
                        VolumeID INTEGER PRIMARY KEY,
                        Title TEXT,
                        Added TEXT,
                        VolumeType INTEGER,
                        IsHashed INTEGER,
                        ArchiveNr TEXT,
                        DriveType TEXT,
                        Loaned_To TEXT,
                        Loaned_Date TEXT,
                        Return_Date TEXT,
                        Category TEXT,
                        Description TEXT,
                        Keywords TEXT,
                        Files INTEGER,
                        Dirs INTEGER,
                        Size INTEGER
                    );

                    CREATE TABLE Items (
                        VolumeID INTEGER,
                        ItemID INTEGER,
                        ParentID INTEGER,
                        ItemType INTEGER,
                        Name TEXT,
                        MimeType TEXT,
                        MetaData TEXT,
                        Note TEXT,
                        Keywords TEXT,
                        Hash TEXT,
                        IsContainer INTEGER,
                        Location TEXT,
                        LastWriteTime TEXT,
                        SymLinkTargetID INTEGER,
                        Size INTEGER,
                        PRIMARY KEY (VolumeID, ItemID)
                    );
                    """;
                command.ExecuteNonQuery();
            }

            return File.ReadAllBytes(tempFile);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    public static byte[] CreateUnsupportedVersionVdbBytes(int version = 5)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"v5-{Guid.NewGuid():N}.vdb");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={tempFile};Pooling=false;"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = $"""
                    CREATE TABLE DatabaseProperties (
                        Name TEXT,
                        Description TEXT,
                        Created TEXT,
                        Version INTEGER,
                        GUID TEXT
                    );

                    INSERT INTO DatabaseProperties VALUES (
                        'Future Collection',
                        'Version 5',
                        '2030-01-01 00:00:00',
                        {version},
                        'guid-v5'
                    );

                    CREATE TABLE Volumes (
                        VolumeID INTEGER PRIMARY KEY,
                        Title TEXT,
                        Added TEXT,
                        VolumeType INTEGER,
                        IsHashed INTEGER,
                        ArchiveNr TEXT,
                        DriveType TEXT,
                        Loaned_To TEXT,
                        Loaned_Date TEXT,
                        Return_Date TEXT,
                        Category TEXT,
                        Description TEXT,
                        Keywords TEXT,
                        Files INTEGER,
                        Dirs INTEGER,
                        Size INTEGER
                    );

                    INSERT INTO Volumes VALUES (1, 'Vol 1', '', 0, 0, '', '', '', '', '', '', '', '', 0, 0, 0);

                    CREATE TABLE Items (
                        VolumeID INTEGER,
                        ItemID INTEGER,
                        ParentID INTEGER,
                        ItemType INTEGER,
                        Name TEXT,
                        MimeType TEXT,
                        MetaData TEXT,
                        Note TEXT,
                        Keywords TEXT,
                        Hash TEXT,
                        IsContainer INTEGER,
                        Location TEXT,
                        LastWriteTime TEXT,
                        SymLinkTargetID INTEGER,
                        Size INTEGER,
                        PRIMARY KEY (VolumeID, ItemID)
                    );
                    """;
                command.ExecuteNonQuery();
            }

            return File.ReadAllBytes(tempFile);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    public static byte[] CreateMissingTablesVdbBytes()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.vdb");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={tempFile};Pooling=false;"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE OtherTable (
                        Id INTEGER PRIMARY KEY,
                        Data TEXT
                    );
                    INSERT INTO OtherTable VALUES (1, 'Test');
                    """;
                command.ExecuteNonQuery();
            }

            return File.ReadAllBytes(tempFile);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
