using System;
using System.IO;
using System.Text;

namespace SkyCD.Plugin.Katalog.Sqlite;

/// <summary>
/// Content-aware recognizer for Katalog File-mode SQLite collections.
/// Requires the SQLite header plus Katalog-specific schema markers
/// (device/file/folder join columns, catalog version column, checksum column)
/// so unrelated SQLite databases are rejected. The scan is bounded.
/// </summary>
public static class KatalogSqliteRecognizer
{
    private static readonly byte[] SqliteHeader = Encoding.ASCII.GetBytes("SQLite format 3\0");
    private const int PeekSize = 65536;

    private static readonly string[] Markers =
    [
        "device_external_id",
        "file_catalog_id",
        "folder_catalog_id",
        "catalog_app_version",
        "checksum_sha256",
        "metadata_extended"
    ];

    private const int AcceptScore = 2;

    /// <summary>
    /// Inspects the stream content and returns true when it looks like a
    /// Katalog File-mode SQLite collection. The position is left unchanged
    /// for seekable streams.
    /// </summary>
    public static bool IsKatalogSqlite(Stream stream, string? fileName = null)
    {
        if (!stream.CanRead)
        {
            return false;
        }

        var initialPosition = stream.CanSeek ? stream.Position : -1;
        try
        {
            var header = new byte[SqliteHeader.Length];
            if (ReadFull(stream, header) < SqliteHeader.Length)
            {
                return false;
            }

            for (var i = 0; i < SqliteHeader.Length; i++)
            {
                if (header[i] != SqliteHeader[i])
                {
                    return false;
                }
            }

            var peek = new byte[PeekSize];
            var totalPeeked = stream.Read(peek, 0, peek.Length);
            var content = Encoding.ASCII.GetString(peek, 0, totalPeeked);

            var score = 0;
            foreach (var marker in Markers)
            {
                if (content.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    score++;
                }
            }

            if (score >= AcceptScore)
            {
                return true;
            }

            return score >= 1 && LooksLikeDatabaseFileName(fileName);
        }
        catch
        {
            return false;
        }
        finally
        {
            if (initialPosition >= 0 && stream.CanSeek)
            {
                stream.Position = initialPosition;
            }
        }
    }

    private static bool LooksLikeDatabaseFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        return fileName.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".sqlite3", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".db3", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadFull(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
