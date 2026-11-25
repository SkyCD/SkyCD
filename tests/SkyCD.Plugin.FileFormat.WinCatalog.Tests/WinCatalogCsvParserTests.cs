using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using SkyCD.Plugin.FileFormat.Csv;
using SkyCD.Plugin.FileFormat.WinCatalog.Csv;
using Xunit;
using SkyCD.Plugin.FileFormat.Csv.Parsing;
using SkyCD.Plugin.FileFormat.WinCatalog.Csv.Import;
using SkyCD.Plugin.FileFormat.WinCatalog.Csv.Models;
using SkyCD.Plugin.FileFormat.WinCatalog.Models;

namespace SkyCD.Plugin.FileFormat.WinCatalog.Tests;

public sealed class WinCatalogCsvParserTests
{
    [Fact]
    public async Task ParseCsv_HandlesQuotedDelimitersAndMultilineComments()
    {
        var csv = "\"Col1\",\"Col2\",\"Comments\"\r\n\"Val1, with comma\",\"Val2; with semi\",\"Line 1\r\nLine 2\r\nLine 3\"\r\n";
        using var reader = new StringReader(csv);

        var parseResult = await CsvParser.ReadCsvAsync(reader, ',');
        Assert.Equal(',', parseResult.Delimiter);
        Assert.Equal(2, parseResult.Rows.Count);

        var dataRow = parseResult.Rows[1];
        Assert.Equal("Val1, with comma", dataRow[0]);
        Assert.Equal("Val2; with semi", dataRow[1]);
        Assert.Contains("Line 1\nLine 2\nLine 3", dataRow[2]);
    }

    [Fact]
    public async Task ParseCsv_HandlesEscapedQuotes()
    {
        var csv = "Name,Comments\r\n\"file.txt\",\"Author said \"\"Hello World\"\"\"\r\n";
        using var reader = new StringReader(csv);

        var parseResult = await CsvParser.ReadCsvAsync(reader);
        Assert.Equal(2, parseResult.Rows.Count);
        Assert.Equal("Author said \"Hello World\"", parseResult.Rows[1][1]);
    }

    [Fact]
    public void ParseSize_HandlesVariousFormatsAndUnits()
    {
        // Raw bytes
        Assert.Equal(1024L, WinCatalogHierarchyBuilder.ParseSize("1024"));
        Assert.Equal(0L, WinCatalogHierarchyBuilder.ParseSize("0"));
        Assert.Equal(0L, WinCatalogHierarchyBuilder.ParseSize(null));
        Assert.Equal(0L, WinCatalogHierarchyBuilder.ParseSize(""));

        // Formatted thousand separators
        Assert.Equal(1048576L, WinCatalogHierarchyBuilder.ParseSize("1,048,576"));
        Assert.Equal(1048576L, WinCatalogHierarchyBuilder.ParseSize("1.048.576"));
        Assert.Equal(1048576L, WinCatalogHierarchyBuilder.ParseSize("1 048 576"));

        // Unit suffixes
        Assert.Equal(512L * 1024, WinCatalogHierarchyBuilder.ParseSize("512 KB"));
        Assert.Equal(1048576L, WinCatalogHierarchyBuilder.ParseSize("1 MB"));
        Assert.Equal((long)Math.Round(1.5 * 1024 * 1024), WinCatalogHierarchyBuilder.ParseSize("1.5 MB"));
        Assert.Equal((long)Math.Round(12.5 * 1024 * 1024), WinCatalogHierarchyBuilder.ParseSize("12,5 MB"));
        Assert.Equal(2L * 1024 * 1024 * 1024, WinCatalogHierarchyBuilder.ParseSize("2 GB"));
    }

    [Fact]
    public void UnescapeWinCatalogTokens_ReplacesSpecialCodes()
    {
        var escaped = "Catalog\\Folder with {@slash} slash and {@dslash} dslash and {@semicolon} semi";
        var unescaped = WinCatalogHierarchyBuilder.UnescapeWinCatalogTokens(escaped);

        Assert.Equal("Catalog\\Folder with / slash and // dslash and ; semi", unescaped);
    }

    [Fact]
    public void Profile_MapsLocalizedHeadersAccurately()
    {
        // English
        Assert.Equal(WinCatalogColumn.CatalogPath, WinCatalogProfile.MapColumn("Catalog Path"));
        Assert.Equal(WinCatalogColumn.FilePath, WinCatalogProfile.MapColumn("File Path"));
        Assert.Equal(WinCatalogColumn.Disk, WinCatalogProfile.MapColumn("Disk"));
        Assert.Equal(WinCatalogColumn.Size, WinCatalogProfile.MapColumn("Size"));

        // German
        Assert.Equal(WinCatalogColumn.CatalogPath, WinCatalogProfile.MapColumn("Katalogpfad"));
        Assert.Equal(WinCatalogColumn.FilePath, WinCatalogProfile.MapColumn("Dateipfad"));
        Assert.Equal(WinCatalogColumn.Disk, WinCatalogProfile.MapColumn("Datenträger"));
        Assert.Equal(WinCatalogColumn.Size, WinCatalogProfile.MapColumn("Dateigröße"));

        // Russian
        Assert.Equal(WinCatalogColumn.CatalogPath, WinCatalogProfile.MapColumn("Путь в каталоге"));
        Assert.Equal(WinCatalogColumn.FilePath, WinCatalogProfile.MapColumn("Путь к файлу"));
        Assert.Equal(WinCatalogColumn.Disk, WinCatalogProfile.MapColumn("Диск"));
        Assert.Equal(WinCatalogColumn.Size, WinCatalogProfile.MapColumn("Размер"));

        // French
        Assert.Equal(WinCatalogColumn.CatalogPath, WinCatalogProfile.MapColumn("Chemin du catalogue"));
        Assert.Equal(WinCatalogColumn.Disk, WinCatalogProfile.MapColumn("Disque"));
        Assert.Equal(WinCatalogColumn.Size, WinCatalogProfile.MapColumn("Taille"));

        // Spanish
        Assert.Equal(WinCatalogColumn.CatalogPath, WinCatalogProfile.MapColumn("Ruta del catálogo"));
        Assert.Equal(WinCatalogColumn.Disk, WinCatalogProfile.MapColumn("Disco"));
        Assert.Equal(WinCatalogColumn.Size, WinCatalogProfile.MapColumn("Tamaño"));
    }

    [Fact]
    public async Task ReadAsync_DecodesUtf8BomFixture()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "WinCatalogCsv", "wincatalog-utf8-bom.csv");
        await using var stream = File.OpenRead(fixturePath);

        var plugin = new WinCatalogCsvPlugin();
        var result = await plugin.ReadAsync(new SkyCD.SDK.Capabilities.FileFormats.FileFormatReadRequest
        {
            FormatId = "wincatalog-csv",
            Source = stream,
            FileName = "wincatalog-utf8-bom.csv"
        });

        Assert.True(result.Success, result.Error);
        var doc = Assert.IsType<WinCatalogCsvReportDocument>(result.Payload);
        Assert.Equal(3, doc.Summary.ImportedRecordsCount);
        Assert.Contains(doc.Rows, row => string.Equals(row["name"]?.ToString(), "Внешний диск", StringComparison.Ordinal));
        Assert.Contains(doc.Rows, row => string.Equals(row["name"]?.ToString(), "договор.pdf", StringComparison.Ordinal));
        Assert.Contains(doc.Rows, row => string.Equals(row["name"]?.ToString(), "лето.jpg", StringComparison.Ordinal));
    }
}
