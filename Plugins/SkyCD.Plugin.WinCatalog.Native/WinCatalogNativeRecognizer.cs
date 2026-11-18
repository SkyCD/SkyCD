using System;
using System.IO;
using System.Text;

namespace SkyCD.Plugin.WinCatalog.Native;

/// <summary>
/// Content-aware recognizer for native WinCatalog catalog databases.
/// Requires the SQLite header plus WinCatalog-specific markers so arbitrary
/// SQLite files are never claimed. Legacy collection extensions are never
/// claimed here; they are reported through
/// <see cref="Exceptions.WinCatalogNativeLegacyProfileException"/> at read time.
/// </summary>
public static class WinCatalogNativeRecognizer
{
    private static readonly byte[] SqliteHeader = Encoding.ASCII.GetBytes("SQLite format 3\0");
    private const int PeekSize = 65536;

    private static readonly string[] Markers =
    [
        "wincatalog",
        "win_catalog",
        "winCatalog",
        "catalog_disks",
        "catalog_folders",
        "catalog_files",
        "catalog_entries",
        "volume_label",
        "physical_location"
    ];

    private const int AcceptScore = 2;

    public static bool IsWinCatalogNative(Stream stream, string? fileName = null)
    {
        if (!stream.CanRead)
        {
            return false;
        }

        if (IsLegacyProfileName(fileName))
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

            return score >= 1 && IsW3catFileName(fileName);
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

    public static void ThrowIfLegacyProfile(string? fileNameOrPath)
    {
        if (IsLegacyProfileName(fileNameOrPath))
        {
            throw new Exceptions.WinCatalogNativeLegacyProfileException(fileNameOrPath);
        }
    }

    public static bool IsLegacyProfileName(string? fileNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrPath))
        {
            return false;
        }

        var name = fileNameOrPath.Trim();
        return name.EndsWith(".wcat", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".wca", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".wcl", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsW3catFileName(string? fileName)
    {
        return !string.IsNullOrWhiteSpace(fileName) &&
            fileName.EndsWith(".w3cat", StringComparison.OrdinalIgnoreCase);
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
