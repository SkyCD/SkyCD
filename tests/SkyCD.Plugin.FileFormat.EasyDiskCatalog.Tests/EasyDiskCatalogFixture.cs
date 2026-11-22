using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SQLitePCL;

namespace SkyCD.Plugin.FileFormat.EasyDiskCatalog.Tests;

/// <summary>
/// Builds .edcdb fixture catalogs independently of the original application, using only
/// the isolated codec bundled with the plugin under test. Covers the documented schema:
/// one disk with nested folders/files, attributes, timestamps, CRC, preview and metadata
/// references, hierarchical categories/locations, one contact with a current and a
/// returned loan, a sibling preview directory with one present and one missing asset,
/// empty/null optionals, an unknown disk type, plus forward-compatibility extras
/// (an unknown column and an unknown table).
/// </summary>
public static class EasyDiskCatalogFixture
{
    public const string CompatibilityPassword = "rVtvGfj3mLr*&!qe";

    public sealed record FixturePaths(string DatabasePath, string PreviewDirectory, string PresentPreview);

    public static FixturePaths Create(string directory, string databaseFileName = "fixture Database.edcdb")
    {
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, databaseFileName);
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }

        raw.SetProvider(new SQLite3Provider_sqlite3mc());

        var key = Encoding.UTF8.GetBytes(CompatibilityPassword);
        var rc = raw.sqlite3_open_v2(
            "file:" + databasePath + "?cipher=rc4",
            out var db,
            raw.SQLITE_OPEN_READWRITE | raw.SQLITE_OPEN_CREATE | raw.SQLITE_OPEN_URI,
            null);
        if (rc != raw.SQLITE_OK || db is null)
        {
            throw new InvalidOperationException($"Cannot create fixture database (result code {rc}).");
        }

        try
        {
            rc = raw.sqlite3_key(db, key);
            if (rc != raw.SQLITE_OK)
            {
                throw new InvalidOperationException($"Cannot key fixture database (result code {rc}).");
            }

            ExecuteScript(db, Schema);
            ExecuteScript(db, Data);
        }
        finally
        {
            raw.sqlite3_close_v2(db);
        }

        var previewDirectory = Path.Combine(directory,
            $"{Path.GetFileNameWithoutExtension(databaseFileName)} Previews");
        Directory.CreateDirectory(previewDirectory);
        var presentPreview = Path.Combine(previewDirectory, "readme.png");
        File.WriteAllBytes(presentPreview, MinimalPng);

        return new FixturePaths(databasePath, previewDirectory, presentPreview);
    }

    public static string CreatePlaintext(string directory, bool withCatalogSchema)
    {
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory,
            withCatalogSchema ? "plain-catalog.db" : "plain-other.db");
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }

        raw.SetProvider(new SQLite3Provider_sqlite3mc());

        var rc = raw.sqlite3_open_v2(
            "file:" + databasePath,
            out var db,
            raw.SQLITE_OPEN_READWRITE | raw.SQLITE_OPEN_CREATE | raw.SQLITE_OPEN_URI,
            null);
        if (rc != raw.SQLITE_OK || db is null)
        {
            throw new InvalidOperationException($"Cannot create plaintext database (result code {rc}).");
        }

        try
        {
            ExecuteScript(db, withCatalogSchema
                ? "CREATE TABLE Disks(id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT);"
                    + "CREATE TABLE Folders(id INTEGER PRIMARY KEY AUTOINCREMENT, DiskID INTEGER, ParentID INTEGER, Name TEXT);"
                    + "CREATE TABLE Files(id INTEGER PRIMARY KEY AUTOINCREMENT, DiskID INTEGER, ParentID INTEGER, Name TEXT);"
                    + "INSERT INTO Disks(Name) VALUES('Plain Disk');"
                : "CREATE TABLE users(id INTEGER PRIMARY KEY, username TEXT);"
                    + "INSERT INTO users(username) VALUES('alice');");
        }
        finally
        {
            raw.sqlite3_close_v2(db);
        }

        return databasePath;
    }

    public static string CreateOtherPassword(string directory)
    {
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "other-password.edcdb");
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }

        raw.SetProvider(new SQLite3Provider_sqlite3mc());

        var rc = raw.sqlite3_open_v2(
            "file:" + databasePath + "?cipher=rc4",
            out var db,
            raw.SQLITE_OPEN_READWRITE | raw.SQLITE_OPEN_CREATE | raw.SQLITE_OPEN_URI,
            null);
        if (rc != raw.SQLITE_OK || db is null)
        {
            throw new InvalidOperationException($"Cannot create database (result code {rc}).");
        }

        try
        {
            rc = raw.sqlite3_key(db, Encoding.UTF8.GetBytes("another-password"));
            if (rc != raw.SQLITE_OK)
            {
                throw new InvalidOperationException($"Cannot key database (result code {rc}).");
            }

            ExecuteScript(db, "CREATE TABLE Disks(id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT);"
                + "CREATE TABLE Folders(id INTEGER PRIMARY KEY AUTOINCREMENT, DiskID INTEGER, ParentID INTEGER, Name TEXT);"
                + "CREATE TABLE Files(id INTEGER PRIMARY KEY AUTOINCREMENT, DiskID INTEGER, ParentID INTEGER, Name TEXT);");
        }
        finally
        {
            raw.sqlite3_close_v2(db);
        }

        return databasePath;
    }

    private static void ExecuteScript(sqlite3 db, string script)
    {
        foreach (var statement in script.Split(';'))
        {
            if (!string.IsNullOrWhiteSpace(statement))
            {
                Execute(db, statement);
            }
        }
    }

    private static void Execute(sqlite3 db, string sql)
    {
        var rc = raw.sqlite3_prepare_v2(db, sql, out var statement);
        if (rc != raw.SQLITE_OK || statement is null)
        {
            throw new InvalidOperationException(
                $"Fixture statement failed to prepare (result code {rc}): {raw.sqlite3_errmsg(db).utf8_to_string()}");
        }

        try
        {
            while ((rc = raw.sqlite3_step(statement)) == raw.SQLITE_ROW)
            {
            }

            if (rc != raw.SQLITE_DONE)
            {
                throw new InvalidOperationException(
                    $"Fixture statement failed (result code {rc}): {raw.sqlite3_errmsg(db).utf8_to_string()}");
            }
        }
        finally
        {
            raw.sqlite3_finalize(statement);
        }
    }

    private const string Schema = """
        CREATE TABLE Disks(id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT, Comment TEXT,
            Folders INTEGER, Files INTEGER, Size INTEGER, FreeSpace INTEGER, Capacity INTEGER,
            Label TEXT, DiskType TEXT, SerialNumber TEXT, DriveLetter TEXT, FileSystem TEXT,
            ComputerName TEXT, ScanDate TEXT, CategoryID INTEGER, LocationID INTEGER, LoanID INTEGER);
        CREATE TABLE Folders(id INTEGER PRIMARY KEY AUTOINCREMENT, DiskID INTEGER, ParentID INTEGER,
            Alias TEXT, Name TEXT, Type TEXT, Path TEXT, Size INTEGER, Folders INTEGER, Files INTEGER,
            Encrypted INTEGER, ReadOnly INTEGER, Hidden INTEGER, System INTEGER, Archive INTEGER,
            Compressed INTEGER, Created TEXT, Modified TEXT, Accessed TEXT, Comment TEXT);
        CREATE TABLE Files(id INTEGER PRIMARY KEY AUTOINCREMENT, DiskID INTEGER, ParentID INTEGER,
            Alias TEXT, Name TEXT, Type TEXT, Path TEXT, Size INTEGER, CRC INTEGER, Preview TEXT,
            Metadata TEXT, Encrypted INTEGER, ReadOnly INTEGER, Hidden INTEGER, System INTEGER,
            Archive INTEGER, Compressed INTEGER, Created TEXT, Modified TEXT, Accessed TEXT, Comment TEXT);
        CREATE TABLE Categories(id INTEGER PRIMARY KEY AUTOINCREMENT, ParentID INTEGER, Name TEXT, Comment TEXT);
        CREATE TABLE Locations(id INTEGER PRIMARY KEY AUTOINCREMENT, ParentID INTEGER, Name TEXT, Comment TEXT);
        CREATE TABLE Contacts(id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT, Address TEXT,
            HomePhone TEXT, MobilePhone TEXT, Email TEXT, Comment TEXT);
        CREATE TABLE Loan(id INTEGER PRIMARY KEY AUTOINCREMENT, DiskID INTEGER, ContactID INTEGER,
            StartDate TEXT, ReturnDate TEXT, Lent INTEGER, Returned TEXT, Comment TEXT);
        ALTER TABLE Files ADD COLUMN CustomFlag TEXT;
        CREATE TABLE CustomNotes(id INTEGER PRIMARY KEY, Note TEXT);
        """;

    private const string Data = """
        INSERT INTO Categories(id, ParentID, Name, Comment) VALUES
            (1, 0, 'Software', ''),
            (2, 1, 'Games', 'favorites');
        INSERT INTO Locations(id, ParentID, Name, Comment) VALUES
            (1, 0, 'Home', NULL),
            (2, 1, 'Shelf A', '');
        INSERT INTO Disks(id, Name, Comment, Folders, Files, Size, FreeSpace, Capacity, Label,
                DiskType, SerialNumber, DriveLetter, FileSystem, ComputerName, ScanDate,
                CategoryID, LocationID, LoanID) VALUES
            (1, 'Backup 2024', 'main backup', 2, 3, 5000000, 1000000, 6000000, 'BACKUP',
                'Unknown', 'SN-123', 'D:', 'NTFS', 'PC-01', '2024-03-10 14:22:31', 2, 2, 1);
        INSERT INTO Folders(id, DiskID, ParentID, Alias, Name, Type, Path, Size, Folders, Files,
                Encrypted, ReadOnly, Hidden, System, Archive, Compressed,
                Created, Modified, Accessed, Comment) VALUES
            (1, 1, 0, 'Docs', 'Documents', 'folder', 'D:\Documents', 3000, 1, 1,
                0, 1, 0, 0, 1, 0,
                '2024-01-05 10:00:00', '2024-02-06 11:30:00', '2024-03-01 09:00:00', 'docs folder'),
            (2, 1, 1, '', 'Projects', 'folder', 'D:\Documents\Projects', 1500, 0, 1,
                0, 0, 1, 0, 0, 0,
                '2024-01-06 10:00:00', '2024-02-07 11:30:00', '2024-03-02 09:00:00', NULL);
        INSERT INTO Files(id, DiskID, ParentID, Alias, Name, Type, Path, Size, CRC, Preview, Metadata,
                Encrypted, ReadOnly, Hidden, System, Archive, Compressed,
                Created, Modified, Accessed, Comment, CustomFlag) VALUES
            (1, 1, 1, '', 'readme.txt', 'txt', 'D:\Documents\readme.txt', 512, 305441741,
                'readme.png', 'Author: Admin', 0, 1, 0, 0, 0, 0,
                '2024-01-05 10:01:00', '2024-02-06 11:31:00', '2024-03-01 09:01:00', '', 'keep-me'),
            (2, 1, 2, 'App', 'app.exe', 'exe', 'D:\Documents\Projects\app.exe', 2048000, 0,
                'missing.png', '', 1, 0, 0, 0, 0, 1,
                '2024-01-06 10:01:00', '2024-02-07 11:31:00', '2024-03-02 09:01:00', 'binary', NULL),
            (3, 1, 0, '', 'loose.iso', 'iso', 'D:\loose.iso', 1024000, 4294967295,
                NULL, NULL, 0, 0, 0, 0, 0, 0,
                '2024-01-07 10:00:00', '2024-01-07 10:00:00', '2024-01-07 10:00:00', NULL, NULL),
            (4, 1, 1, '', '', '', '', 0, 0, NULL, NULL, 0, 0, 0, 0, 0, 0, NULL, NULL, NULL, NULL, NULL);
        INSERT INTO Contacts(id, Name, Address, HomePhone, MobilePhone, Email, Comment) VALUES
            (1, 'Jane Doe', '1 Main St', '555-0100', '555-0200', 'jane@example.com', 'borrower');
        INSERT INTO Loan(id, DiskID, ContactID, StartDate, ReturnDate, Lent, Returned, Comment) VALUES
            (1, 1, 1, '2024-04-01', '', 1, '0', 'on loan'),
            (2, 1, 1, '2024-01-10', '2024-01-20', 1, '1', 'returned ok');
        INSERT INTO CustomNotes(id, Note) VALUES (1, 'forward compatible note');
        """;

    private static readonly byte[] MinimalPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
        0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41,
        0x54, 0x08, 0xD7, 0x63, 0xF8, 0xFF, 0xFF, 0x3F,
        0x00, 0x05, 0xFE, 0x02, 0xFE, 0xDC, 0xCC, 0x59,
        0xE7, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
        0x44, 0xAE, 0x42, 0x60, 0x82
    ];
}
