using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SkyCD.SDK.Capabilities.FileFormats;

namespace SkyCD.Plugin.FileFormat.AbeMeda.Tests;

/// <summary>
/// Generates synthetic NeoFinder-style text exports at runtime.
/// </summary>
/// <remarks>
/// The layout mirrors the documented "Text with Tab" export: a configurable delimiter,
/// an optional header row, one row per item and locale-dependent dates. Connection pooling
/// is disabled so the generated files can be read back on Windows.
/// </remarks>
public static class NeoFinderTextFixture
{
    private static readonly string[] StandardHeader =
        ["Name", "Kind", "Size", "Where", "Volume", "Catalog", "Date Modified", "Comments"];

    public static MemoryStream CreateStandardExport(char delimiter = '\t', Encoding? encoding = null)
    {
        return Build(
            StandardHeader,
            [
                ["Docs", "Folder", "", "/Archive/Docs", "Archive", "Photos", "2026-01-05 10:00:00", ""],
                ["readme.txt", "Plain Text", "1024", "/Archive/Docs/readme.txt", "Archive", "Photos", "2026-01-06 11:00:00", "hello"],
                ["foto.jpg", "JPEG image", "2.5 MB", "/Archive/Docs/foto.jpg", "Archive", "Photos", "2026-01-07 12:00:00", ""],
                ["crème brûlée.txt", "Plain Text", "512", "/Archive/Docs/crème brûlée.txt", "Archive", "Photos", "08.01.2026 09:00:00", "unicode"],
            ],
            delimiter,
            encoding);
    }

    public static MemoryStream CreateMacPathExport()
    {
        return Build(
            ["Name", "Kind", "Size", "Where", "Volume"],
            [
                ["Docs", "Folder", "", "Archive:Docs:", "Archive"],
                ["note.txt", "Plain Text", "100", "Archive:Docs:note.txt", "Archive"],
            ],
            '\t');
    }

    public static MemoryStream CreateVolumeOnlyExport()
    {
        return Build(
            ["Name", "Kind", "Size", "Volume", "Comments"],
            [
                ["loose.txt", "Plain Text", "10", "Archive", "no path info"],
                ["other.txt", "Plain Text", "20", "Archive", ""],
            ],
            '\t');
    }

    public static MemoryStream CreateGermanExport()
    {
        return Build(
            ["Name", "Art", "Größe", "Pfad", "Volumen", "Geändert"],
            [
                ["Bilder", "Ordner", "", "/Archiv/Bilder", "Archiv", "05.01.2026 10:00:00"],
                ["foto.jpg", "JPEG-Bild", "2,5 MB", "/Archiv/Bilder/foto.jpg", "Archiv", "06.01.2026 11:00:00"],
            ],
            ';');
    }

    public static MemoryStream CreateMultiCatalogExport()
    {
        return Build(
            StandardHeader,
            [
                ["a.txt", "Plain Text", "1", "/A/a.txt", "DiskA", "CatA", "2026-01-01 00:00:00", ""],
                ["b.txt", "Plain Text", "2", "/B/b.txt", "DiskB", "CatB", "2026-01-02 00:00:00", ""],
            ],
            '\t');
    }

    public static MemoryStream CreateDuplicateNameExport()
    {
        return Build(
            ["Name", "Kind", "Size", "Where", "Volume"],
            [
                ["readme.txt", "Plain Text", "1", "/Media/A/readme.txt", "Media"],
                ["readme.txt", "Plain Text", "2", "/Media/B/readme.txt", "Media"],
            ],
            '\t');
    }

    public static MemoryStream CreateMalformedExport()
    {
        return Build(
            StandardHeader,
            [
                ["", "Plain Text", "10", "/Archive/bad.txt", "Archive", "Photos", "2026-01-01 00:00:00", ""],
                ["good.txt", "Plain Text", "10", "/Archive/good.txt", "Archive", "Photos", "2026-01-01 00:00:00", ""],
            ],
            '\t');
    }

    public static MemoryStream CreateEmptyExport()
    {
        return Build(StandardHeader, [], '\t');
    }

    public static MemoryStream CreateHeaderOnlyNameExport()
    {
        return Build(["Name", "Kind"], [["lonely.txt", "Plain Text"]], '\t');
    }

    public static MemoryStream CreateHeaderlessExport()
    {
        return Build(null, [["readme.txt", "Plain Text", "10"]], '\t');
    }

    public static MemoryStream CreateAmbiguousExport()
    {
        return Build(["Name", "File Name", "Where"], [["a.txt", "a.txt", "/V/a.txt"]], '\t');
    }

    public static MemoryStream CreateUnrelatedText()
    {
        var bytes = Encoding.UTF8.GetBytes("Just some notes\nSecond line\n");
        return new MemoryStream(bytes, writable: false);
    }

    public static MemoryStream CreateWinCatalogCsvLookalike()
    {
        var bytes = Encoding.UTF8.GetBytes("Catalog Path,Name,Size\n/Vol/a.txt,a.txt,10\n");
        return new MemoryStream(bytes, writable: false);
    }

    public static byte[] TruncatedBytes() => Encoding.ASCII.GetBytes("Na");

    private static MemoryStream Build(string[]? header, List<string[]> rows, char delimiter, Encoding? encoding = null)
    {
        encoding ??= new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var builder = new StringBuilder();
        if (header is not null)
        {
            builder.AppendLine(string.Join(delimiter, header.Select(Escape)));
        }

        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(delimiter, row.Select(Escape)));
        }

        return new MemoryStream(encoding.GetBytes(builder.ToString()), writable: false);
    }

    private static string Escape(string value)
    {
        if (value.Contains('"') || value.Contains('\t') || value.Contains(',') || value.Contains(';') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    public static FileFormatReadRequest ReadRequest(Stream source, string fileName = "export.txt")
    {
        return new FileFormatReadRequest
        {
            Source = source,
            FormatId = "neofinder-text",
            FileName = fileName,
            SourcePath = fileName
        };
    }
}
