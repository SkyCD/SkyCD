using System;
using System.IO;
using System.Text;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.Csv;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker;
using SkyCD.Plugin.FileFormat.DiskExplorer.Delimited;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.Plugin.FileFormat.DiskCatalogMaker.Tests;

public sealed class DiskCatalogMakerDetectionTests
{
    private readonly DiskCatalogMakerPlugin plugin = new();

    [Fact]
    public void SupportedFormat_HasExpectedMetadata()
    {
        var format = plugin.SupportedFormat;
        Assert.Equal("diskcatalogmaker-text", format.FormatId);
        Assert.Equal("DiskCatalogMaker Text Export", format.DisplayName);
        Assert.Contains(".txt", format.Extensions);
        Assert.Contains(".tsv", format.Extensions);
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
    public void CanHandleContent_ReturnsFalseForArbitraryText()
    {
        var arbitrary = "First Name\tLast Name\tEmail\r\nJohn\tDoe\tjohn@example.com\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(arbitrary));

        var result = plugin.CanHandleContent(stream, "users.txt");
        Assert.False(result);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_ReturnsFalseWhenPathColumnMissing()
    {
        var noPath = "Name\tSize\tKind\r\nfile.txt\t100\tPlain text\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(noPath));

        var result = plugin.CanHandleContent(stream, "export.txt");
        Assert.False(result);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_ReturnsFalseWhenNameColumnMissing()
    {
        var noName = "Path\tSize\tKind\r\n/Volumes/Disk/file.txt\t100\tPlain text\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(noName));

        var result = plugin.CanHandleContent(stream, "export.txt");
        Assert.False(result);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_ReturnsFalseForWinCatalogCsvReport()
    {
        var winCatalogCsv = "Disk,Catalog Path,Name,Size,Type,Modified\r\n\"Disk 1\",\"Catalog\\Disk 1\\File.txt\",\"File.txt\",100,File,2026-01-01\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(winCatalogCsv));

        var result = plugin.CanHandleContent(stream, "export.txt");
        Assert.False(result);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_ReturnsFalseForDiskExplorerDelimitedReport()
    {
        var diskExplorer = "VOLUME\tPATH\tITEMTYPE\tNAME\tSIZE\tDATE\tTIME\tTYPE\tDESC\r\nMyDisk\tPhotos\tFile\tpic.jpg\t100\t2026-01-01\t10:00\tJPEG\tA photo\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(diskExplorer));

        var result = plugin.CanHandleContent(stream, "export.txt");
        Assert.False(result);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_ReturnsTrueForStandardDiskCatalogMakerExport()
    {
        var export = "Name\tPath\tSize\tKind\tDate Modified\r\npic.jpg\t/Volumes/MyDisk/Photos/pic.jpg\t100\tJPEG image\t2026-01-01 10:00:00\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(export));

        var result = plugin.CanHandleContent(stream, "export.txt");
        Assert.True(result);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_ReturnsTrueForReorderedCommaExport()
    {
        // Name stays first (DiskCatalogMaker fixes it); the remaining attributes are user-ordered.
        var export = "Name,Date Modified,Size,Kind,Path\r\npic.jpg,2026-01-01,100,JPEG image,/Volumes/Disk/pic.jpg\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(export));

        var result = plugin.CanHandleContent(stream, "export.csv");
        Assert.True(result);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void FileFormatManager_DisambiguatesDiskCatalogMakerFromDiskExplorer()
    {
        // Content disambiguation runs in registration order (see FileFormatManager);
        // the assertions below pin that the two formats do not steal each other when
        // DiskCatalogMaker is consulted first.
        var providers = new IFileFormatPluginCapability[]
        {
            new DiskCatalogMakerPlugin(),
            new DiskExplorerDelimitedPlugin()
        };
        var manager = new FileFormatManager(providers);

        var diskCatalogMaker = "Name\tPath\tSize\tKind\tDate Modified\r\npic.jpg\t/Volumes/MyDisk/Photos/pic.jpg\t100\tJPEG image\t2026-01-01\r\n";
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(diskCatalogMaker)))
        {
            var capability = manager.GetInstanceFor("export.txt", stream);
            Assert.Equal("diskcatalogmaker-text", capability.SupportedFormat.FormatId);
            Assert.Equal(0, stream.Position);
        }

        var diskExplorer = "VOLUME\tPATH\tITEMTYPE\tNAME\tSIZE\tDATE\tTIME\tTYPE\tDESC\r\nMyDisk\tPhotos\tFile\tpic.jpg\t100\t2026-01-01\t10:00\tJPEG\tA photo\r\n";
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(diskExplorer)))
        {
            var capability = manager.GetInstanceFor("export.txt", stream);
            Assert.Equal("disk-explorer-delimited", capability.SupportedFormat.FormatId);
            Assert.Equal(0, stream.Position);
        }
    }
}
