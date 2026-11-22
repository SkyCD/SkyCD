using System;
using System.IO;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.SmartCDCatalogPro;
using Xunit;

namespace SkyCD.Plugin.FileFormat.SmartCDCatalogPro.Tests;

public sealed class SmartCdCatalogProDetectionTests
{
    private readonly SmartCdCatalogProPlugin plugin = new();

    [Fact]
    public async Task CanHandleContent_WithCatalog_ReturnsTrue()
    {
        using var stream = await SmartCdCatalogProFixture.CreateCatalogAsync();
        Assert.True(plugin.CanHandleContent(stream, "catalog.scdc"));
    }

    [Fact]
    public async Task CanHandleContent_WithoutFileName_ReturnsTrue()
    {
        using var stream = await SmartCdCatalogProFixture.CreateCatalogAsync();
        Assert.True(plugin.CanHandleContent(stream, fileName: null));
    }

    [Fact]
    public async Task CanHandleContent_WithMisleadingExtension_ReturnsTrue()
    {
        using var stream = await SmartCdCatalogProFixture.CreateCatalogAsync();
        Assert.True(plugin.CanHandleContent(stream, "catalog.bin"));
    }

    [Fact]
    public async Task CanHandleContent_RestoresStreamPosition()
    {
        using var stream = await SmartCdCatalogProFixture.CreateCatalogAsync();
        stream.Position = 10;
        Assert.True(plugin.CanHandleContent(stream, "catalog.scdc"));
        Assert.Equal(10, stream.Position);
    }

    [Fact]
    public async Task CanHandleContent_WithUnrelatedJetDatabase_ReturnsFalse()
    {
        using var stream = await SmartCdCatalogProFixture.CreateUnrelatedJetDatabaseAsync();
        Assert.False(plugin.CanHandleContent(stream, "catalog.scdc"));
    }

    [Fact]
    public async Task CanHandleContent_WithWrongPassword_ReturnsFalse()
    {
        using var stream = await SmartCdCatalogProFixture.CreateAsync("wrong-password-2026", async writer =>
        {
            await SmartCdCatalogProFixture.CreateStandardSchemaAsync(writer);
            await SmartCdCatalogProFixture.SeedStandardDataAsync(writer);
        });
        Assert.False(plugin.CanHandleContent(stream, "catalog.scdc"));
    }

    [Fact]
    public async Task CanHandleContent_WithTruncatedDatabase_ReturnsFalse()
    {
        using var catalog = await SmartCdCatalogProFixture.CreateCatalogAsync();
        var bytes = catalog.ToArray();
        using var stream = new MemoryStream(bytes[..Math.Min(4096, bytes.Length)], writable: false);
        Assert.False(plugin.CanHandleContent(stream, "catalog.scdc"));
    }

    [Fact]
    public void CanHandleContent_WithRandomBytes_ReturnsFalse()
    {
        using var stream = new MemoryStream("this is not a Jet database at all"u8.ToArray(), writable: false);
        Assert.False(plugin.CanHandleContent(stream, "catalog.scdc"));
    }

    [Fact]
    public void SupportedFormat_IsReadOnlyScdc()
    {
        var format = plugin.SupportedFormat;
        Assert.Equal("smart-cd-catalog-pro-scdc", format.FormatId);
        Assert.Contains(".scdc", format.Extensions);
        Assert.Contains("application/vnd.smart-cd-catalog-pro.scdc", format.MimeTypes);
        Assert.True(format.CanRead);
        Assert.False(format.CanWrite);
    }

    [Fact]
    public async Task WriteAsync_IsReadOnly()
    {
        var result = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "smart-cd-catalog-pro-scdc",
            Target = new MemoryStream(),
            Payload = new object()
        });

        Assert.False(result.Success);
        Assert.Contains("read-only", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
