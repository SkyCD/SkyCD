using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using SkyCD.Plugin.FileFormat.AbeMeda.Text;
using SkyCD.Plugin.FileFormat.AbeMeda.Text.Import;
using SkyCD.Plugin.FileFormat.AbeMeda.Text.Models;

namespace SkyCD.Plugin.FileFormat.AbeMeda.Tests;

public sealed class NeoFinderTextReaderTests
{
    private readonly NeoFinderTextPlugin plugin = new();

    [Fact]
    public async Task ReadAsync_WithStandardExport_BuildsHierarchy()
    {
        using var stream = NeoFinderTextFixture.CreateStandardExport();
        var result = await plugin.ReadAsync(NeoFinderTextFixture.ReadRequest(stream));
        Assert.True(result.Success);
        var document = Assert.IsType<NeoFinderTextReportDocument>(result.Payload);
        Assert.True(document.Count >= 5);

        var rows = document.Rows.ToList();
        Assert.Contains(rows, r => r["kind"]?.ToString() == "media");
        Assert.Contains(rows, r => r["name"]?.ToString() == "readme.txt");
        Assert.Contains(rows, r => r["name"]?.ToString() == "crème brûlée.txt");
        Assert.Equal(4, document.Summary.ImportedRecordsCount);
        Assert.False(document.Summary.IsPartialReport);
        Assert.Equal('\t', document.Summary.DetectedDelimiter);
        Assert.Contains("Where", document.Summary.DetectedColumns);
    }

    [Fact]
    public async Task ReadAsync_WithMacPaths_PreservesHierarchy()
    {
        using var stream = NeoFinderTextFixture.CreateMacPathExport();
        var result = await plugin.ReadAsync(NeoFinderTextFixture.ReadRequest(stream));
        Assert.True(result.Success);
        var document = Assert.IsType<NeoFinderTextReportDocument>(result.Payload);
        var rows = document.Rows.ToList();
        Assert.Contains(rows, r => r["name"]?.ToString() == "Docs" && r["kind"]?.ToString() == "folder");
        Assert.Contains(rows, r => r["name"]?.ToString() == "note.txt");
    }

    [Fact]
    public async Task ReadAsync_WithGermanExport_MapsLocalizedColumns()
    {
        using var stream = NeoFinderTextFixture.CreateGermanExport();
        var result = await plugin.ReadAsync(NeoFinderTextFixture.ReadRequest(stream, "export.txt"));
        Assert.True(result.Success);
        var document = Assert.IsType<NeoFinderTextReportDocument>(result.Payload);
        Assert.Equal(';', document.Summary.DetectedDelimiter);
        Assert.Contains(document.Rows, r => r["name"]?.ToString() == "foto.jpg");
        var photo = document.Rows.First(r => r["name"]?.ToString() == "foto.jpg");
        Assert.Equal("2621440", photo["sizeBytes"]?.ToString());
    }

    [Fact]
    public async Task ReadAsync_WithMultipleCatalogs_CreatesMediaPerCatalog()
    {
        using var stream = NeoFinderTextFixture.CreateMultiCatalogExport();
        var result = await plugin.ReadAsync(NeoFinderTextFixture.ReadRequest(stream));
        Assert.True(result.Success);
        var document = Assert.IsType<NeoFinderTextReportDocument>(result.Payload);
        var media = document.Rows.Where(r => r["kind"]?.ToString() == "media").ToList();
        Assert.Equal(2, media.Count);
    }

    [Fact]
    public async Task ReadAsync_WithoutLocationColumns_WarnsAndFlattens()
    {
        using var stream = NeoFinderTextFixture.CreateHeaderOnlyNameExport();
        var result = await plugin.ReadAsync(NeoFinderTextFixture.ReadRequest(stream));
        Assert.True(result.Success);
        var document = Assert.IsType<NeoFinderTextReportDocument>(result.Payload);
        Assert.True(document.Summary.IsPartialReport);
        Assert.Contains(document.Summary.SkippedReasons, r => r.StartsWith("Warning:"));
    }

    [Fact]
    public async Task ReadAsync_WithHeaderlessExport_ReturnsAmbiguousError()
    {
        using var stream = NeoFinderTextFixture.CreateHeaderlessExport();
        var result = await plugin.ReadAsync(NeoFinderTextFixture.ReadRequest(stream));
        Assert.False(result.Success);
        Assert.Contains("ambiguous", result.Error, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_WithDuplicateSemanticColumns_ReturnsAmbiguousError()
    {
        using var stream = NeoFinderTextFixture.CreateAmbiguousExport();
        var result = await plugin.ReadAsync(NeoFinderTextFixture.ReadRequest(stream));
        Assert.False(result.Success);
        Assert.Contains("ambiguous", result.Error, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_WithEmptyExport_ReturnsEmptyError()
    {
        using var stream = NeoFinderTextFixture.CreateEmptyExport();
        var result = await plugin.ReadAsync(NeoFinderTextFixture.ReadRequest(stream));
        Assert.False(result.Success);
        Assert.Contains("no importable entries", result.Error);
    }

    [Fact]
    public async Task ReadAsync_WithMalformedRows_SkipsWithoutDataLoss()
    {
        using var stream = NeoFinderTextFixture.CreateMalformedExport();
        var result = await plugin.ReadAsync(NeoFinderTextFixture.ReadRequest(stream));
        Assert.True(result.Success);
        var document = Assert.IsType<NeoFinderTextReportDocument>(result.Payload);
        Assert.Contains(document.Rows, r => r["name"]?.ToString() == "good.txt");
        Assert.Equal(1, document.Summary.SkippedRecordsCount);
    }

    [Fact]
    public async Task ReadAsync_SupportsCancellation()
    {
        using var stream = NeoFinderTextFixture.CreateStandardExport();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<System.OperationCanceledException>(() =>
            plugin.ReadAsync(NeoFinderTextFixture.ReadRequest(stream), cts.Token));
    }

    [Theory]
    [InlineData("1024", 1024)]
    [InlineData("2.5 MB", 2621440)]
    [InlineData("2,5 MB", 2621440)]
    [InlineData("1 KB", 1024)]
    [InlineData("—", 0)]
    [InlineData("", 0)]
    public void ParseSize_HandlesExportFormats(string raw, long expected)
    {
        Assert.Equal(expected, NeoFinderTextHierarchyBuilder.ParseSize(raw));
    }

    [Theory]
    [InlineData("2026-01-06 11:00:00", "2026-01-06 11:00:00")]
    [InlineData("06.01.2026 11:00:00", "2026-01-06 11:00:00")]
    [InlineData("06/01/2026 11:00:00", "2026-06-01 11:00:00")]
    public void NormalizeDate_HandlesConfiguredFormats(string raw, string expected)
    {
        Assert.Equal(expected, NeoFinderTextHierarchyBuilder.NormalizeDate(raw));
    }
}
