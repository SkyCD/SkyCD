using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace SkyCD.Plugin.CdCat.Hcf.Tests;

public sealed class CdCatHcfDetectionTests
{
    private readonly CdCatHcfPlugin plugin = new();

    [Fact]
    public void CanHandleContent_ValidGzipHcf_ReturnsTrue_AndPreservesPosition()
    {
        var gzipBytes = CdCatTestHelper.CreateGzipXml(CdCatTestHelper.SampleV21Xml);
        using var stream = new MemoryStream(gzipBytes);
        stream.Position = 0;

        var result = plugin.CanHandleContent(stream, "sample.hcf");

        Assert.True(result);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_ValidUncompressedXml_ReturnsTrue_AndPreservesPosition()
    {
        var xmlBytes = Encoding.UTF8.GetBytes(CdCatTestHelper.SampleV21Xml);
        using var stream = new MemoryStream(xmlBytes);
        stream.Position = 0;

        var result = plugin.CanHandleContent(stream, "sample.xml");

        Assert.True(result);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_NonCdCatGzip_ReturnsFalse()
    {
        var randomText = "This is just plain text compressed with gzip, not a cdcat catalog.";
        var gzipBytes = CdCatTestHelper.CreateGzipXml(randomText);
        using var stream = new MemoryStream(gzipBytes);

        var result = plugin.CanHandleContent(stream, "other.gz");

        Assert.False(result);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_NonCdCatXml_ReturnsFalse()
    {
        var genericXml = "<?xml version=\"1.0\"?><root><item name=\"test\" /></root>";
        var xmlBytes = Encoding.UTF8.GetBytes(genericXml);
        using var stream = new MemoryStream(xmlBytes);

        var result = plugin.CanHandleContent(stream, "other.xml");

        Assert.False(result);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_EmptyStream_ReturnsFalse()
    {
        using var stream = new MemoryStream();
        var result = plugin.CanHandleContent(stream, "empty.hcf");
        Assert.False(result);
    }

    [Fact]
    public void CanHandleContent_CorruptGzip_ReturnsFalse()
    {
        var corruptBytes = new byte[] { 0x1F, 0x8B, 0x00, 0x01, 0xFF };
        using var stream = new MemoryStream(corruptBytes);
        var result = plugin.CanHandleContent(stream, "corrupt.hcf");
        Assert.False(result);
        Assert.Equal(0, stream.Position);
    }
}
