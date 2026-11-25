using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace SkyCD.Plugin.FileFormat.BrokenX.Tests;

public sealed class BxdDetectionTests
{
    private readonly BxdPlugin plugin = new();

    [Fact]
    public void CanHandleContent_WithCanonicalEmpty_ReturnsTrue()
    {
        using var stream = BxdFixture.StreamOf(BxdFixture.CanonicalEmpty());
        Assert.True(plugin.CanHandleContent(stream, "catalog.bxd"));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_WithOneItem_ReturnsTrue()
    {
        using var stream = BxdFixture.StreamOf(BxdFixture.Serialize(BxdFixture.OneItemDatabase()));
        Assert.True(plugin.CanHandleContent(stream, "catalog.bxd"));
    }

    [Fact]
    public void CanHandleContent_WithBadSignature_ReturnsFalse()
    {
        using var stream = BxdFixture.StreamOf(BxdFixture.Mutate(BxdFixture.CanonicalEmpty(), b => b[0] = 0x00));
        Assert.False(plugin.CanHandleContent(stream, "catalog.bxd"));
    }

    [Fact]
    public void CanHandleContent_WithUnsupportedVersion_ReturnsFalse()
    {
        using var stream = BxdFixture.StreamOf(BxdFixture.Mutate(BxdFixture.CanonicalEmpty(), b => b[21] = 0x41));
        Assert.False(plugin.CanHandleContent(stream, "catalog.bxd"));
    }

    [Fact]
    public void CanHandleContent_WithTruncatedHeader_ReturnsFalse()
    {
        using var stream = BxdFixture.StreamOf(BxdFixture.CanonicalEmpty()[..10]);
        Assert.False(plugin.CanHandleContent(stream, "catalog.bxd"));
    }

    [Fact]
    public void CanHandleContent_WithUnrelatedBytes_ReturnsFalse()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, writable: false);
        Assert.False(plugin.CanHandleContent(stream, "catalog.bxd"));
    }

    [Fact]
    public void CanHandleContent_RestoresPosition()
    {
        using var stream = BxdFixture.StreamOf(BxdFixture.CanonicalEmpty());
        stream.Position = 5;
        plugin.CanHandleContent(stream, "catalog.bxd");
        Assert.Equal(5, stream.Position);
    }

    [Fact]
    public void SupportedFormat_ExposesBxdExtension()
    {
        Assert.Contains(".bxd", plugin.SupportedFormat.Extensions);
        Assert.True(plugin.SupportedFormat.CanRead);
        Assert.True(plugin.SupportedFormat.CanWrite);
    }

    [Fact]
    public async Task WriteAsync_WithUnsupportedPayload_ReturnsError()
    {
        var result = await plugin.WriteAsync(new SkyCD.SDK.Capabilities.FileFormats.FileFormatWriteRequest
        {
            FormatId = "brokenx-bxd",
            Target = new MemoryStream(),
            Payload = new object()
        });
        Assert.False(result.Success);
    }
}
