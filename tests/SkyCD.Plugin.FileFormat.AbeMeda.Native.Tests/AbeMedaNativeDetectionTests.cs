using System.IO;
using System.Linq;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.Plugin.FileFormat.AbeMeda.Native.Tests;

public sealed class AbeMedaNativeDetectionTests
{
    private readonly AbeMedaNativePlugin plugin = new();

    [Fact]
    public void CanHandleContent_WithNativeSignatureAndSentinel_ReturnsTrue()
    {
        using var stream = AbeMedaNativeFixture.CreateCatalog();
        Assert.True(plugin.CanHandleContent(stream, "demo.cdw"));
    }

    [Fact]
    public void CanHandleContent_WithWrongSignature_ReturnsFalse()
    {
        using var stream = AbeMedaNativeFixture.CreateCatalog(catalog => catalog.Magic = "XXXXXXXX");
        Assert.False(plugin.CanHandleContent(stream, "demo.cdw"));
    }

    [Fact]
    public void CanHandleContent_WithBadSentinel_ReturnsFalse()
    {
        using var stream = AbeMedaNativeFixture.CreateCatalog(catalog => catalog.SentinelOverride = 0);
        Assert.False(plugin.CanHandleContent(stream, "demo.cdw"));
    }

    [Fact]
    public void CanHandleContent_WithTruncatedHeader_ReturnsFalse()
    {
        using var stream = AbeMedaNativeFixture.CreateCatalog(catalog => catalog.TruncateTo = 0x50);
        Assert.False(plugin.CanHandleContent(stream, "demo.cdw"));
    }

    [Fact]
    public void CanHandleContent_PreservesStreamPosition()
    {
        using var stream = AbeMedaNativeFixture.CreateCatalog();
        stream.Position = 17;

        _ = plugin.CanHandleContent(stream, "demo.cdw");

        Assert.Equal(17, stream.Position);
    }

    [Fact]
    public void SupportedFormat_ExposesNativeExtensionsAndIsReadOnly()
    {
        var service = new FileFormatManager([plugin]);

        Assert.Contains(service.GetOpenFormats(),
            format => format.FormatId == "abemeda-native"
                && format.Extensions.Contains(".cdw")
                && format.Extensions.Contains(".abemeda7")
                && format.Extensions.Contains(".neofinder7"));
        Assert.DoesNotContain(service.GetSaveFormats(),
            format => format.FormatId == "abemeda-native");
        Assert.True(plugin.SupportedFormat.CanRead);
        Assert.False(plugin.SupportedFormat.CanWrite);
    }
}
