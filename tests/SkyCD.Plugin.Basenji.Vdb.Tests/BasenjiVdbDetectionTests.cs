using System.IO;
using Xunit;

namespace SkyCD.Plugin.Basenji.Vdb.Tests;

public sealed class BasenjiVdbDetectionTests
{
    [Fact]
    public void SupportedFormat_MatchesDescriptor()
    {
        var plugin = new BasenjiVdbPlugin();

        Assert.Equal("basenji-vdb", plugin.SupportedFormat.FormatId);
        Assert.Contains(".vdb", plugin.SupportedFormat.Extensions);
        Assert.True(plugin.SupportedFormat.CanRead);
        Assert.False(plugin.SupportedFormat.CanWrite);
    }

    [Fact]
    public void CanHandleContent_WithValidVdbBytes_ReturnsTrue()
    {
        var plugin = new BasenjiVdbPlugin();
        var bytes = BasenjiTestHelper.CreateSampleVdbBytes();
        using var stream = new MemoryStream(bytes);

        var canHandle = plugin.CanHandleContent(stream, "catalog.vdb");

        Assert.True(canHandle);
    }

    [Fact]
    public void CanHandleContent_WithoutFileName_InspectsContentAndReturnsTrue()
    {
        var plugin = new BasenjiVdbPlugin();
        var bytes = BasenjiTestHelper.CreateSampleVdbBytes();
        using var stream = new MemoryStream(bytes);

        var canHandle = plugin.CanHandleContent(stream, null);

        Assert.True(canHandle);
    }

    [Fact]
    public void CanHandleContent_WithNonSqliteBytes_ReturnsFalse()
    {
        var plugin = new BasenjiVdbPlugin();
        using var stream = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8]);

        var canHandle = plugin.CanHandleContent(stream, "test.txt");

        Assert.False(canHandle);
    }

    [Fact]
    public void CanHandleContent_PreservesStreamPosition()
    {
        var plugin = new BasenjiVdbPlugin();
        var bytes = BasenjiTestHelper.CreateSampleVdbBytes();
        using var stream = new MemoryStream(bytes);
        stream.Position = 10;

        _ = plugin.CanHandleContent(stream, "test.vdb");

        Assert.Equal(10, stream.Position);
    }
}
