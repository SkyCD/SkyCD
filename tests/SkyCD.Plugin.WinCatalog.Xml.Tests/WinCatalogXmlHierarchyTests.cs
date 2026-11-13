using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.WinCatalog.Xml;
using Xunit;

namespace SkyCD.Plugin.WinCatalog.Xml.Tests;

public sealed class WinCatalogXmlHierarchyTests
{
    private readonly WinCatalogXmlPlugin plugin = new();

    private async Task<FileFormatReadResult> ReadFixtureAsync(string fileName)
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "WinCatalogXml", fileName);
        await using var stream = File.OpenRead(fixturePath);
        return await plugin.ReadAsync(new FileFormatReadRequest
        {
            Source = stream,
            FormatId = "wincatalog-xml",
            FileName = fileName
        });
    }

    private static async Task<WinCatalogXmlReportDocument> ReadDocumentAsync(string fileName)
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "WinCatalogXml", fileName);
        await using var stream = File.OpenRead(fixturePath);
        var result = await new WinCatalogXmlPlugin().ReadAsync(new FileFormatReadRequest
        {
            Source = stream,
            FormatId = "wincatalog-xml",
            FileName = fileName
        });
        Assert.True(result.Success, result.Error);
        return Assert.IsType<WinCatalogXmlReportDocument>(result.Payload);
    }

    [Fact]
    public async Task ReadAsync_NestedEnglishReport_ReconstructsHierarchyAndMetadata()
    {
        var document = await ReadDocumentAsync("wincatalog-nested-en.xml");

        Assert.Equal("Nested", document.Summary.ImportMode);
        Assert.Equal("WinCatalog", document.Summary.DetectedRootElement);
        Assert.Equal("2026", document.Summary.DetectedSchemaVersion);
        Assert.False(document.Summary.IsPartialReport);
        Assert.Empty(document.Summary.UnsupportedFields);
        Assert.Equal(10, document.Summary.ImportedItemsCount);

        var rows = document.ToDictionary(row => (string)row["nodeId"]!, row => row);

        var disk1 = rows["media:Archive Disk 1"];
        Assert.Equal("media", disk1["kind"]);
        Assert.Null(disk1["parentId"]);
        Assert.Equal("734003200", disk1["sizeBytes"]);
        var disk1Properties = Assert.IsType<Dictionary<string, object?>>(disk1["properties"]);
        Assert.Equal("ARCH1", disk1Properties["VolumeLabel"]);
        Assert.Equal("Backup archive disk", disk1Properties["Comments"]);

        var documents = rows["folder:Archive Disk 1/Documents"];
        Assert.Equal("folder", documents["kind"]);
        Assert.Equal("media:Archive Disk 1", documents["parentId"]);

        var manuals = rows["folder:Archive Disk 1/Documents/Manuals"];
        Assert.Equal("folder:Archive Disk 1/Documents", manuals["parentId"]);

        var readme = rows["file:Archive Disk 1/Documents/readme.txt"];
        Assert.Equal("file", readme["kind"]);
        Assert.Equal("1024", readme["sizeBytes"]);
        var readmeProperties = Assert.IsType<Dictionary<string, object?>>(readme["properties"]);
        Assert.Equal("Documentation \"v1.0\", initial draft", readmeProperties["Comments"]);
        Assert.Equal("docs", readmeProperties["Tags"]);
        Assert.Equal("4", readmeProperties["Rating"]);
        Assert.Equal("d41d8cd98f00b204e9800998ecf8427e", readmeProperties["MD5"]);
        Assert.Equal("2026-01-10 12:05:00", readmeProperties["DateModified"]);
        Assert.Equal("2026", readmeProperties["WinCatalogSchemaVersion"]);

        var catalogDat = rows["file:Archive Disk 1/catalog.dat"];
        Assert.Equal("media:Archive Disk 1", catalogDat["parentId"]);

        var sample = rows["file:Archive Disk 2/Photos/2026/sample.jpg"];
        Assert.Equal("1572864", sample["sizeBytes"]);
        var sampleProperties = Assert.IsType<Dictionary<string, object?>>(sample["properties"]);
        Assert.Equal("8A9B0C1D", sampleProperties["CRC32"]);
        Assert.Equal("2026-01-15 08:00:00", Assert.IsType<Dictionary<string, object?>>(rows["folder:Archive Disk 2/Photos"]["properties"])["DateCreated"]);
    }

    [Fact]
    public async Task ReadAsync_UnicodeRussianReport_PreservesNamesAndNormalizesLocaleFormats()
    {
        var document = await ReadDocumentAsync("wincatalog-nested-ru.xml");

        var rows = document.ToDictionary(row => (string)row["nodeId"]!, row => row);
        Assert.Contains("media:Музыка", rows.Keys);
        Assert.Contains("folder:Музыка/Классика", rows.Keys);
        Assert.Contains("file:Музыка/Классика/Чайковский - Лебединое озеро.flac", rows.Keys);

        var flac = rows["file:Музыка/Классика/Чайковский - Лебединое озеро.flac"];
        Assert.Equal("13107200", flac["sizeBytes"]);
        var properties = Assert.IsType<Dictionary<string, object?>>(flac["properties"]);
        Assert.Equal("2025-02-15 18:30:00", properties["DateModified"]);
        Assert.Equal("Русская запись", properties["Comments"]);
        Assert.Equal("классика", properties["Tags"]);
        Assert.Equal("2024", document.Summary.DetectedSchemaVersion);
    }

    [Fact]
    public async Task ReadAsync_EmptyFieldsFallback_CleanlyImportsMissingValues()
    {
        var document = await ReadDocumentAsync("wincatalog-empty-fields.xml");

        var rows = document.ToDictionary(row => (string)row["nodeId"]!, row => row);
        Assert.Contains("media:Empty Disk", rows.Keys);
        Assert.Contains("folder:Empty Disk/Empty Folder", rows.Keys);

        var noMetadata = rows["file:Empty Disk/Empty Folder/no-metadata.txt"];
        Assert.Equal("0", noMetadata["sizeBytes"]);
        var properties = Assert.IsType<Dictionary<string, object?>>(noMetadata["properties"]);
        Assert.DoesNotContain("Comments", properties.Keys);
        Assert.DoesNotContain("DateModified", properties.Keys);

        var withComment = rows["file:Empty Disk/Empty Folder/with-comment.txt"];
        Assert.Equal("512", withComment["sizeBytes"]);
        var withCommentProperties = Assert.IsType<Dictionary<string, object?>>(withComment["properties"]);
        Assert.Equal("Only a comment", withCommentProperties["Comments"]);
    }

    [Fact]
    public async Task ReadAsync_FlatSearchResults_DelegatesToSharedHierarchyLogic()
    {
        var document = await ReadDocumentAsync("wincatalog-flat-search-results.xml");

        Assert.Equal("Flat", document.Summary.ImportMode);
        Assert.True(document.Summary.IsPartialReport);
        Assert.Equal(3, document.Summary.ImportedItemsCount);

        var rows = document.ToDictionary(row => (string)row["nodeId"]!, row => row);
        Assert.Contains("media:Archive Disk 1", rows.Keys);
        Assert.Contains("media:Archive Disk 2", rows.Keys);

        var readme = rows.Values.Single(row => (string)row["name"]! == "readme.txt");
        Assert.Equal("1024", readme["sizeBytes"]);
        Assert.Equal("file", readme["kind"]);

        var manualIndex = rows.Values.Single(row => (string)row["name"]! == "manual-index.txt");
        var manualProperties = Assert.IsType<Dictionary<string, object?>>(manualIndex["properties"]);
        Assert.Equal("manuals", manualProperties["Tags"]);
    }

    [Fact]
    public async Task ReadAsync_MalformedXml_ReturnsFailureWithDiagnostic()
    {
        var result = await ReadFixtureAsync("wincatalog-malformed.xml");

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public async Task ReadAsync_DtdAndExternalEntities_AreRejected()
    {
        var result = await ReadFixtureAsync("wincatalog-dtd-entity.xml");

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public async Task ReadAsync_EmptyWinCatalogRoot_IsRejectedAsUnrecognizable()
    {
        using var stream = new MemoryStream("<WinCatalog version=\"2026\"/>"u8.ToArray());
        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            Source = stream,
            FormatId = "wincatalog-xml",
            FileName = "empty.xml"
        });

        Assert.False(result.Success);
        Assert.Contains("does not contain a recognizable WinCatalog XML report", result.Error);
    }

    [Fact]
    public async Task ReadAsync_UnsupportedSchemaVersion_IsReportedWithDiagnostic()
    {
        var xml =
            "<WinCatalog version=\"ancient-formats-only\">\r\n" +
            "  <Disks>\r\n" +
            "    <Disk Name=\"Disk 1\">\r\n" +
            "      <File Name=\"Doc.txt\" Size=\"100\"/>\r\n" +
            "    </Disk>\r\n" +
            "  </Disks>\r\n" +
            "</WinCatalog>";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            Source = stream,
            FormatId = "wincatalog-xml",
            FileName = "old.xml"
        });

        Assert.False(result.Success);
        Assert.Contains("unsupported schema version", result.Error);
    }

    [Fact]
    public async Task ReadAsync_WriteIsNotSupported()
    {
        using var stream = new MemoryStream();
        var result = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            Target = stream,
            FormatId = "wincatalog-xml",
            FileName = "export.xml",
            Payload = new List<Dictionary<string, object?>>()
        });

        Assert.False(result.Success);
        Assert.Contains("read-only", result.Error);
    }
}
