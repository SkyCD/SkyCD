using System;
using System.IO;
using System.Text;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Csv;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Plugin.WinCatalog.Csv;
using Xunit;

namespace SkyCD.Plugin.WinCatalog.Csv.Tests;

public sealed class WinCatalogCsvDetectionTests
{
    private readonly WinCatalogCsvPlugin plugin = new();

    [Fact]
    public void SupportedFormat_HasExpectedMetadata()
    {
        var format = plugin.SupportedFormat;
        Assert.Equal("wincatalog-csv", format.FormatId);
        Assert.Equal("WinCatalog CSV Report", format.DisplayName);
        Assert.Contains(".csv", format.Extensions);
        Assert.True(format.CanRead);
        Assert.False(format.CanWrite);
    }

    [Fact]
    public void CanHandleContent_ReturnsFalseForGenericSkyCdCsv()
    {
        var genericCsv = "NodeId,ParentId,Kind,Name,SizeBytes\r\n1,,folder,Root,0\r\n2,1,file,sample.txt,100\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(genericCsv));

        var result = plugin.CanHandleContent(stream, "catalog.csv");
        Assert.False(result);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_ReturnsFalseForArbitraryCsv()
    {
        var arbitraryCsv = "First Name,Last Name,Email,Age\r\nJohn,Doe,john@example.com,30\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(arbitraryCsv));

        var result = plugin.CanHandleContent(stream, "users.csv");
        Assert.False(result);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_ReturnsTrueForStandardWinCatalogCsv()
    {
        var winCatalogCsv = "Disk,Catalog Path,Name,Size,Type,Modified\r\n\"Disk 1\",\"Catalog\\Disk 1\\File.txt\",\"File.txt\",100,File,2026-01-01\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(winCatalogCsv));

        var result = plugin.CanHandleContent(stream, "export.csv");
        Assert.True(result);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_ReturnsTrueForLocalizedGermanWinCatalogCsv()
    {
        var germanCsv = "Datenträger;Katalogpfad;Dateiname;Dateigröße;Dateityp\r\n\"Disk 1\";\"Katalog\\Disk 1\\Datei.txt\";\"Datei.txt\";100;Datei\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(germanCsv));

        var result = plugin.CanHandleContent(stream, "bericht.csv");
        Assert.True(result);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_ReturnsTrueForFilePathWithDisk()
    {
        var filePathCsv = "File Path,Filename,Size,Disk\r\n\"D:\\Photos\\vacation.jpg\",\"vacation.jpg\",1024,\"Photos Disk\"\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(filePathCsv));

        var result = plugin.CanHandleContent(stream, "search-results.csv");
        Assert.True(result);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void FileFormatManager_ResolvesWinCatalogCsvWhenContentMatches()
    {
        var providers = new IFileFormatPluginCapability[]
        {
            new CsvCatalogPlugin(),
            new WinCatalogCsvPlugin()
        };
        var manager = new FileFormatManager(providers);

        // Without stream or content: fallback to generic csv
        Assert.Equal("skycd-csv", manager.GetInstanceFor("file.csv").SupportedFormat.FormatId);

        // With WinCatalog stream: resolves to wincatalog-csv
        var winCatalogCsv = "Disk,Catalog Path,Name,Size\r\n\"Disk 1\",\"Catalog\\Disk 1\\Doc.txt\",\"Doc.txt\",100\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(winCatalogCsv));
        var capability = manager.GetInstanceFor("export.csv", stream);
        Assert.Equal("wincatalog-csv", capability.SupportedFormat.FormatId);
        Assert.Equal(0, stream.Position);
    }
}
