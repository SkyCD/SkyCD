using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace SkyCD.Plugin.FileFormat.NeoFinder.Text.Tests;

public sealed class NeoFinderTextDetectionTests
{
    private readonly NeoFinderTextPlugin plugin = new();

    [Fact]
    public void CanHandleContent_WithTabExport_ReturnsTrue()
    {
        using var stream = NeoFinderTextFixture.CreateStandardExport();
        Assert.True(plugin.CanHandleContent(stream, "export.txt"));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_WithCommaExport_ReturnsTrue()
    {
        using var stream = NeoFinderTextFixture.CreateStandardExport(',');
        Assert.True(plugin.CanHandleContent(stream, "export.txt"));
    }

    [Theory]
    [InlineData("export.tsv")]
    [InlineData("export.txt")]
    public void CanHandleContent_HonoursFileName(string fileName)
    {
        using var stream = NeoFinderTextFixture.CreateStandardExport();
        Assert.True(plugin.CanHandleContent(stream, fileName));
    }

    [Fact]
    public void CanHandleContent_WithUnrelatedText_ReturnsFalse()
    {
        using var stream = NeoFinderTextFixture.CreateUnrelatedText();
        Assert.False(plugin.CanHandleContent(stream, "notes.txt"));
    }

    [Fact]
    public void CanHandleContent_WithSkyCdGenericCsv_ReturnsFalse()
    {
        using var stream = new MemoryStream(
            System.Text.Encoding.UTF8.GetBytes("NodeId,ParentId,Kind,Name,SizeBytes\n1,,media,Root,0\n"),
            writable: false);
        Assert.False(plugin.CanHandleContent(stream, "catalog.txt"));
    }

    [Fact]
    public void CanHandleContent_WithWinCatalogCsv_ReturnsFalse()
    {
        using var stream = NeoFinderTextFixture.CreateWinCatalogCsvLookalike();
        Assert.False(plugin.CanHandleContent(stream, "report.csv"));
    }

    [Fact]
    public void CanHandleContent_WithTruncatedHeader_ReturnsFalse()
    {
        using var stream = new MemoryStream(NeoFinderTextFixture.TruncatedBytes(), writable: false);
        Assert.False(plugin.CanHandleContent(stream, "export.txt"));
    }

    [Fact]
    public void CanHandleContent_RestoresPosition()
    {
        using var stream = NeoFinderTextFixture.CreateStandardExport();
        stream.Position = 5;
        plugin.CanHandleContent(stream, "export.txt");
        Assert.Equal(5, stream.Position);
    }

    [Fact]
    public void SupportedFormat_ExposesTextExtensions()
    {
        Assert.Contains(".txt", plugin.SupportedFormat.Extensions);
        Assert.Contains(".tsv", plugin.SupportedFormat.Extensions);
        Assert.True(plugin.SupportedFormat.CanRead);
        Assert.False(plugin.SupportedFormat.CanWrite);
    }

    [Fact]
    public async Task WriteAsync_IsReadOnly()
    {
        var result = await plugin.WriteAsync(new SkyCD.Plugin.Abstractions.Capabilities.FileFormats.FileFormatWriteRequest
        {
            FormatId = "neofinder-text",
            Target = new MemoryStream(),
            Payload = new object()
        });
        Assert.False(result.Success);
    }
}
