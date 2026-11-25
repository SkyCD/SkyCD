using System.IO;
using System.Threading.Tasks;
using SkyCD.Plugin.FileFormat.WinCatalog.Native;
using Xunit;

namespace SkyCD.Plugin.FileFormat.WinCatalog.Tests;

public sealed class WinCatalogNativeDetectionTests
{
    private readonly WinCatalogNativePlugin plugin = new();

    [Fact]
    public void CanHandleContent_WithWinCatalogSqlite_ReturnsTrue()
    {
        using var stream = WinCatalogNativeFixture.CreateCatalog();
        Assert.True(plugin.CanHandleContent(stream, "catalog.w3cat"));
    }

    [Fact]
    public void CanHandleContent_WithUnrelatedSqlite_ReturnsFalse()
    {
        using var stream = WinCatalogNativeFixture.CreateUnrelatedSqlite();
        Assert.False(plugin.CanHandleContent(stream, "other.db"));
        Assert.False(plugin.CanHandleContent(stream, "catalog.w3cat"));
    }

    [Fact]
    public void CanHandleContent_WithTruncatedHeader_ReturnsFalse()
    {
        using var stream = new MemoryStream(WinCatalogNativeFixture.TruncatedHeader(), writable: false);
        Assert.False(plugin.CanHandleContent(stream, "catalog.w3cat"));
    }

    [Fact]
    public void CanHandleContent_WithLegacyExtension_ReturnsFalse()
    {
        using var stream = WinCatalogNativeFixture.CreateCatalog();
        Assert.False(plugin.CanHandleContent(stream, "collection.wcat"));
        Assert.False(plugin.CanHandleContent(stream, "collection.wca"));
        Assert.False(plugin.CanHandleContent(stream, "collection.wcl"));
    }

    [Fact]
    public void SupportedFormat_ExposesNativeAndLegacyExtensions()
    {
        Assert.Contains(".w3cat", plugin.SupportedFormat.Extensions);
        Assert.Contains(".wcat", plugin.SupportedFormat.Extensions);
        Assert.Contains(".wca", plugin.SupportedFormat.Extensions);
        Assert.Contains(".wcl", plugin.SupportedFormat.Extensions);
        Assert.True(plugin.SupportedFormat.CanRead);
        Assert.False(plugin.SupportedFormat.CanWrite);
    }

    [Fact]
    public async Task ReadAsync_WithLegacyProfile_ReturnsActionableError()
    {
        using var stream = WinCatalogNativeFixture.CreateCatalog();
        var result = await plugin.ReadAsync(WinCatalogNativeFixture.ReadRequest(stream, "legacy.wcat"));
        Assert.False(result.Success);
        Assert.Contains("Legacy WinCatalog profile", result.Error);
    }

    [Fact]
    public async Task ReadAsync_WithUnrelatedSqlite_ReturnsUnsupportedSchema()
    {
        using var stream = WinCatalogNativeFixture.CreateUnrelatedSqlite();
        var result = await plugin.ReadAsync(WinCatalogNativeFixture.ReadRequest(stream));
        Assert.False(result.Success);
        Assert.Contains("schema", result.Error, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteAsync_IsReadOnly()
    {
        var result = await plugin.WriteAsync(new SkyCD.SDK.Capabilities.FileFormats.FileFormatWriteRequest
        {
            FormatId = "wincatalog-native",
            Target = new MemoryStream(),
            Payload = new object()
        });
        Assert.False(result.Success);
    }
}
