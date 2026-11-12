using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.WinCatalog.Csv;
using SkyCD.Plugin.WinCatalog.Csv.Exceptions;
using Xunit;

namespace SkyCD.Plugin.WinCatalog.Csv.Tests;

public sealed class WinCatalogCsvHierarchyTests
{
    private readonly WinCatalogCsvPlugin plugin = new();

    [Fact]
    public async Task ReadAsync_MultipleMediaAndDuplicateFilenames_PreservedDistinctly()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "WinCatalogCsv", "wincatalog-standard-en.csv");
        await using var stream = File.OpenRead(fixturePath);

        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "wincatalog-csv",
            Source = stream,
            FileName = "wincatalog-standard-en.csv"
        });

        Assert.True(result.Success, result.Error);
        var doc = Assert.IsType<WinCatalogCsvReportDocument>(result.Payload);

        // Verify media roots
        var mediaNodes = doc.Rows.Where(r => string.Equals(r["kind"]?.ToString(), "media", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(2, mediaNodes.Count);
        Assert.Contains(mediaNodes, m => string.Equals(m["name"]?.ToString(), "Archive Disk 1", StringComparison.Ordinal));
        Assert.Contains(mediaNodes, m => string.Equals(m["name"]?.ToString(), "Archive Disk 2", StringComparison.Ordinal));

        // Verify duplicate filenames in distinct paths
        var readmeNodes = doc.Rows.Where(r => string.Equals(r["name"]?.ToString(), "readme.txt", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, readmeNodes.Count);

        // Every node must have a unique stable Id
        var ids = doc.Rows.Select(r => r["nodeId"]?.ToString()).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());

        // Verify each duplicate readme has a distinct parent
        var parentIds = readmeNodes.Select(r => r["parentId"]?.ToString()).Distinct().ToList();
        Assert.Equal(3, parentIds.Count);

        // Verify metadata preservation on a leaf node
        var samplePhoto = doc.Rows.Single(r => string.Equals(r["name"]?.ToString(), "sample.jpg", StringComparison.Ordinal));
        var props = Assert.IsAssignableFrom<Dictionary<string, object?>>(samplePhoto["properties"]);
        Assert.Equal("vacation,2026", props["Tags"]);
        Assert.Equal("Summer photo", props["Comments"]);
        Assert.Equal("5", props["Rating"]);
        Assert.Equal("c4ca4238a0b923820dcc509a6f75849b", props["MD5"]);
        Assert.Equal("6b86b273ff34fce19d6b804eff5a3f5747ada4eaa22f1d49c01e52ddb7875b4b", props["SHA256"]);
    }

    [Fact]
    public async Task ReadAsync_GermanLocalizedCsv_PreservesHierarchyAndMetadata()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "WinCatalogCsv", "wincatalog-localized-de.csv");
        await using var stream = File.OpenRead(fixturePath);

        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "wincatalog-csv",
            Source = stream,
            FileName = "wincatalog-localized-de.csv"
        });

        Assert.True(result.Success, result.Error);
        var doc = Assert.IsType<WinCatalogCsvReportDocument>(result.Payload);

        var media = doc.Rows.Single(r => string.Equals(r["kind"]?.ToString(), "media", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Sicherung 2026", media["name"]);

        var bilanz = doc.Rows.Single(r => string.Equals(r["name"]?.ToString(), "bilanz.pdf", StringComparison.Ordinal));
        Assert.Equal(Convert.ToInt64(Math.Round(12.5 * 1024 * 1024)), Convert.ToInt64(bilanz["sizeBytes"]));

        var notizenNodes = doc.Rows.Where(r => string.Equals(r["name"]?.ToString(), "notizen.txt", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, notizenNodes.Count);
        Assert.NotEqual(notizenNodes[0]["parentId"], notizenNodes[1]["parentId"]);
    }

    [Fact]
    public async Task ReadAsync_PartialSearchResultReport_SynthesizesFoldersAndFlagsPartial()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "WinCatalogCsv", "wincatalog-filepath-partial.csv");
        await using var stream = File.OpenRead(fixturePath);

        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "wincatalog-csv",
            Source = stream,
            FileName = "wincatalog-filepath-partial.csv"
        });

        Assert.True(result.Success, result.Error);
        var doc = Assert.IsType<WinCatalogCsvReportDocument>(result.Payload);

        Assert.True(doc.Summary.IsPartialReport);
        Assert.Equal(4, doc.Summary.ImportedRecordsCount);

        // Verify intermediate folder nodes were synthesized
        var folderNodes = doc.Rows.Where(r => string.Equals(r["kind"]?.ToString(), "folder", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.NotEmpty(folderNodes);
        Assert.Contains(folderNodes, f => string.Equals(f["name"]?.ToString(), "Docs", StringComparison.Ordinal));
        Assert.Contains(folderNodes, f => string.Equals(f["name"]?.ToString(), "Specs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadAsync_MissingEssentialPathColumns_FailsWithDescriptiveError()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "WinCatalogCsv", "wincatalog-missing-essential-path.csv");
        await using var stream = File.OpenRead(fixturePath);

        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "wincatalog-csv",
            Source = stream,
            FileName = "wincatalog-missing-essential-path.csv"
        });

        Assert.False(result.Success);
        Assert.Contains("missing essential path columns", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_MalformedRows_ImportsValidAndSummarizesSkipped()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "WinCatalogCsv", "wincatalog-malformed-rows.csv");
        await using var stream = File.OpenRead(fixturePath);

        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "wincatalog-csv",
            Source = stream,
            FileName = "wincatalog-malformed-rows.csv"
        });

        Assert.True(result.Success, result.Error);
        var doc = Assert.IsType<WinCatalogCsvReportDocument>(result.Payload);

        Assert.Equal(3, doc.Summary.ImportedRecordsCount);
        Assert.Equal(1, doc.Summary.SkippedRecordsCount);
        Assert.NotEmpty(doc.Summary.SkippedReasons);
        Assert.Contains("Row 4", doc.Summary.SkippedReasons[0]);
    }
}
