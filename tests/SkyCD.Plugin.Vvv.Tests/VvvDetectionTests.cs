using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using Xunit;

namespace SkyCD.Plugin.Vvv.Tests;

public sealed class VvvDetectionTests
{
    private readonly VvvPlugin plugin = new();

    private async Task<FileFormatReadResult> ReadFixtureAsync(string fileName)
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Vvv", fileName);
        await using var stream = File.OpenRead(fixturePath);
        return await plugin.ReadAsync(new FileFormatReadRequest
        {
            Source = stream,
            FormatId = "vvv",
            FileName = fileName
        });
    }

    [Fact]
    public void SupportedFormat_HasExpectedMetadata()
    {
        var format = plugin.SupportedFormat;

        Assert.Equal("vvv", format.FormatId);
        Assert.Equal("VVV Export (*.csv)", format.DisplayName);
        Assert.Contains(".csv", format.Extensions);
        Assert.Contains(".txt", format.Extensions);
        Assert.True(format.CanRead);
        Assert.False(format.CanWrite);
    }

    [Fact]
    public void CanHandleContent_ReturnsTrueForPhysicalHeaders()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Vvv", "physical-comma.csv");
        using var stream = File.OpenRead(fixturePath);

        Assert.True(plugin.CanHandleContent(stream, "export.csv"));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_ReturnsTrueForVirtualHeaders()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Vvv", "virtual-comma.csv");
        using var stream = File.OpenRead(fixturePath);

        Assert.True(plugin.CanHandleContent(stream, "virtual.csv"));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_ReturnsFalseForHeaderlessExport()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Vvv", "physical-tab.tsv");
        using var stream = File.OpenRead(fixturePath);

        Assert.False(plugin.CanHandleContent(stream, "notabs.csv"));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_ReturnsFalseForGenericCsv()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            "NodeId,ParentId,Kind,Name,SizeBytes\r\n1,,media,Root,0\r\n"));

        Assert.False(plugin.CanHandleContent(stream, "catalog.csv"));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_ReturnsFalseForNonCsv()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<collection><item /></collection>"));

        Assert.False(plugin.CanHandleContent(stream, "collection.xml"));
    }

    [Fact]
    public async Task ReadAsync_PhysicalExport_ReturnsRows()
    {
        var result = await ReadFixtureAsync("physical-comma.csv");

        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<VvvReportDocument>(result.Payload);
        Assert.Equal(8, document.Count);
        Assert.Equal(VvvExportScope.Physical, document.Summary.Scope);
        Assert.Equal(',', document.Summary.Delimiter);
        Assert.True(document.Summary.HasHeader);
        Assert.Equal(2, document.Summary.Volumes);
        Assert.Equal(3, document.Summary.Files);
        Assert.Equal(0, document.Summary.SkippedRows);
    }

    [Fact]
    public async Task ReadAsync_HeaderlessExport_SucceedsByExplicitSelection()
    {
        var result = await ReadFixtureAsync("physical-tab.tsv");

        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<VvvReportDocument>(result.Payload);
        Assert.False(document.Summary.HasHeader);
        Assert.Equal(2, document.Summary.Files);
    }

    [Fact]
    public async Task ReadAsync_VirtualExport_NamesRootAfterFile()
    {
        var result = await ReadFixtureAsync("virtual-comma.csv");

        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<VvvReportDocument>(result.Payload);
        Assert.Equal("virtual-comma", document.Rows[0]["name"]);
        Assert.Equal(VvvExportScope.Virtual, document.Summary.Scope);
    }

    [Fact]
    public async Task ReadAsync_AmbiguousExport_ReturnsFailure()
    {
        var result = await ReadFixtureAsync("ambiguous.csv");

        Assert.False(result.Success);
        Assert.Contains("ambiguous", result.Error);
    }

    [Fact]
    public async Task WriteAsync_IsNotSupported()
    {
        await using var target = new MemoryStream();
        var result = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            Target = target,
            FormatId = "vvv",
            Payload = new object()
        });

        Assert.False(result.Success);
    }
}
