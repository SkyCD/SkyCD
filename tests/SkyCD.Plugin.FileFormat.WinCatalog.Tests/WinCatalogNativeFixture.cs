using System;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.WinCatalog.Native;

namespace SkyCD.Plugin.FileFormat.WinCatalog.Tests;

public static class WinCatalogNativeFixture
{
    public static MemoryStream CreateCatalog(Action<SqliteConnection>? seed = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"wincat-fixture-{Guid.NewGuid():N}.db");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=false"))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = """
                        CREATE TABLE catalog_disks (disk_id INTEGER PRIMARY KEY, disk_name TEXT, volume_label TEXT);
                        CREATE TABLE catalog_entries (entry_id INTEGER PRIMARY KEY, entry_name TEXT, full_path TEXT, entry_type TEXT, file_size INTEGER, modified TEXT, comments TEXT, tags TEXT);
                        INSERT INTO catalog_disks (disk_name, volume_label) VALUES ('Archive Disk 1', 'ARCHIVE1');
                        """;
                    command.ExecuteNonQuery();
                }

                seed?.Invoke(connection);
                if (seed is null)
                {
                    using var insert = connection.CreateCommand();
                    insert.CommandText = """
                        INSERT INTO catalog_entries (entry_name, full_path, entry_type, file_size, modified, comments, tags) VALUES
                          ('Docs', 'Archive Disk 1/Docs', 'folder', 0, '2026-01-05 10:00:00', NULL, NULL),
                          ('readme.txt', 'Archive Disk 1/Docs/readme.txt', 'file', 1024, '2026-01-06 11:00:00', 'hello', 'docs'),
                          ('foto.jpg', 'Archive Disk 1/Docs/foto.jpg', 'file', 2048, '2026-01-07 12:00:00', NULL, NULL),
                          ('crème brûlée.txt', 'Archive Disk 1/Docs/crème brûlée.txt', 'file', 512, '2026-01-08 09:00:00', 'unicode', 'u');
                        """;
                    insert.ExecuteNonQuery();
                }
            }

            var bytes = File.ReadAllBytes(path);
            return new MemoryStream(bytes, writable: false);
        }
        finally
        {
            TryDelete(path);
        }
    }

    public static MemoryStream CreateEmptyCatalog()
    {
        return CreateCatalog(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM catalog_entries;";
            command.ExecuteNonQuery();
        });
    }

    public static MemoryStream CreateUnrelatedSqlite()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wincat-unrelated-{Guid.NewGuid():N}.db");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=false"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE notes (id INTEGER PRIMARY KEY, body TEXT); INSERT INTO notes (body) VALUES ('hello');";
                command.ExecuteNonQuery();
            }

            return new MemoryStream(File.ReadAllBytes(path), writable: false);
        }
        finally
        {
            TryDelete(path);
        }
    }

    public static MemoryStream CreateNestedCatalog()
    {
        return CreateCatalog(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO catalog_entries (entry_name, full_path, entry_type, file_size, modified, comments, tags) VALUES
                  ('A', 'Media/A', 'folder', 0, NULL, NULL, NULL),
                  ('B', 'Media/A/B', 'folder', 0, NULL, NULL, NULL),
                  ('C', 'Media/A/B/C', 'folder', 0, NULL, NULL, NULL),
                  ('deep.txt', 'Media/A/B/C/deep.txt', 'file', 100, NULL, NULL, NULL),
                  ('sib1.txt', 'Media/sib1.txt', 'file', 10, NULL, NULL, NULL),
                  ('sib2.txt', 'Media/sib2.txt', 'file', 20, NULL, NULL, NULL);
                """;
            command.ExecuteNonQuery();
        });
    }

    public static FileFormatReadRequest ReadRequest(Stream source, string fileName = "catalog.w3cat")
    {
        return new FileFormatReadRequest
        {
            Source = source,
            FormatId = "wincatalog-native",
            FileName = fileName,
            SourcePath = fileName
        };
    }

    public static byte[] TruncatedHeader() => Encoding.ASCII.GetBytes("SQLite forma");

    private static void TryDelete(string path)
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
    }
}
