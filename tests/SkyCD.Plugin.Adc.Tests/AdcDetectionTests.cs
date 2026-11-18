using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace SkyCD.Plugin.Adc.Tests;

public sealed class AdcDetectionTests
{
    private readonly AdcPlugin plugin = new();

    [Theory]
    [InlineData("catalog.cat")]
    [InlineData("catalog.adc")]
    [InlineData("CATALOG.CAT")]
    public void CanHandleContent_WithSupportedExtensionAndHeader_ReturnsTrue(string fileName)
    {
        using var stream = StreamOf(AdcFixtureWriter.Write(AdcFixtures.NestedCatalog()));
        Assert.True(plugin.CanHandleContent(stream, fileName));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_WithoutFileName_UsesContentOnly()
    {
        using var stream = StreamOf(AdcFixtureWriter.Write(AdcFixtures.NestedCatalog()));
        Assert.True(plugin.CanHandleContent(stream));
    }

    [Fact]
    public void CanHandleContent_WithUnsupportedExtension_ReturnsFalse()
    {
        using var stream = StreamOf(AdcFixtureWriter.Write(AdcFixtures.NestedCatalog()));
        Assert.False(plugin.CanHandleContent(stream, "catalog.bin"));
    }

    [Fact]
    public void CanHandleContent_WithWindowsSecurityCatalog_ReturnsFalse()
    {
        using var stream = StreamOf(AdcFixtures.WindowsSecurityCatalog());
        Assert.False(plugin.CanHandleContent(stream, "security.cat"));
    }

    [Fact]
    public void CanHandleContent_WithBadSignature_ReturnsFalse()
    {
        using var stream = StreamOf(AdcFixtures.CorruptNested());
        Assert.False(plugin.CanHandleContent(stream, "catalog.cat"));
    }

    [Fact]
    public void CanHandleContent_WithUnsupportedVersion_ReturnsFalse()
    {
        using var stream = StreamOf(AdcFixtures.UnsupportedNested());
        Assert.False(plugin.CanHandleContent(stream, "catalog.cat"));
    }

    [Fact]
    public void CanHandleContent_WithTruncatedHeader_ReturnsFalse()
    {
        using var stream = StreamOf(AdcFixtureWriter.Write(AdcFixtures.NestedCatalog())[..20]);
        Assert.False(plugin.CanHandleContent(stream, "catalog.cat"));
    }

    [Fact]
    public void CanHandleContent_RestoresPosition()
    {
        using var stream = StreamOf(AdcFixtureWriter.Write(AdcFixtures.NestedCatalog()));
        stream.Position = 7;
        plugin.CanHandleContent(stream, "catalog.cat");
        Assert.Equal(7, stream.Position);
    }

    [Fact]
    public void SupportedFormat_IsImportOnlyAndAdvertisesCat()
    {
        Assert.Contains(".cat", plugin.SupportedFormat.Extensions);
        Assert.Contains(".adc", plugin.SupportedFormat.Extensions);
        Assert.Contains("Advanced Disk Catalog", plugin.SupportedFormat.DisplayName);
        Assert.True(plugin.SupportedFormat.CanRead);
        Assert.False(plugin.SupportedFormat.CanWrite);
    }

    [Fact]
    public async Task WriteAsync_IsRejected()
    {
        var result = await plugin.WriteAsync(new SkyCD.Plugin.Abstractions.Capabilities.FileFormats.FileFormatWriteRequest
        {
            FormatId = "advanced-disk-catalog",
            Target = new MemoryStream(),
            Payload = new object()
        });

        Assert.False(result.Success);
        Assert.Contains("read-only", result.Error);
    }

    private static MemoryStream StreamOf(byte[] bytes) => new(bytes, writable: false);
}
