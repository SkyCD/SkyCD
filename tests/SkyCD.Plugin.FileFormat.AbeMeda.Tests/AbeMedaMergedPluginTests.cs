using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Lifecycle;
using SkyCD.Plugin.FileFormat.AbeMeda.Native;
using SkyCD.Plugin.FileFormat.Xml;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.Plugin.FileFormat.AbeMeda.Tests;

public sealed class AbeMedaMergedPluginTests
{
    private static readonly Assembly MergedAssembly = typeof(AbeMedaPlugin).Assembly;

    private static FileFormatManager CreateMergedManager()
    {
        return new FileFormatManager([new XmlCatalogPlugin(), new AbeMedaPlugin(), new AbeMedaNativePlugin()]);
    }

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "AbeMeda", name);

    [Fact]
    public void Assembly_PinsOnePluginIdForBothCapabilities()
    {
        Assert.Equal("SkyCD.Plugin.AbeMeda", MergedAssembly.GetCustomAttribute<PluginIdAttribute>()?.Id);
        Assert.Same(MergedAssembly, typeof(AbeMedaNativePlugin).Assembly);
    }

    [Fact]
    public void Discovery_ReturnsOnePluginCoveringBothFormats()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(MergedAssembly);

        Assert.Equal("SkyCD.Plugin.AbeMeda", plugin.Id);
        var formatIds = plugin.Capabilities
            .OfType<IFileFormatPluginCapability>()
            .Select(capability => capability.SupportedFormat.FormatId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(["abemeda-native", "abemeda-xml"], formatIds);
    }

    [Fact]
    public void OpenFormats_ExposeBothCapabilitiesFromOneManager()
    {
        var formats = CreateMergedManager().GetOpenFormats();

        Assert.Contains(formats, format =>
            format.FormatId == "abemeda-xml" && format.Extensions.Contains(".xml"));
        Assert.Contains(formats, format =>
            format.FormatId == "abemeda-native"
            && format.Extensions.Contains(".cdw")
            && format.Extensions.Contains(".abemeda7")
            && format.Extensions.Contains(".neofinder7"));
    }

    [Fact]
    public void ContentResolution_SelectsAbeMedaXmlAgainstOtherXmlImporters()
    {
        using var stream = File.OpenRead(FixturePath("nested-two-volumes.xml"));

        var capability = CreateMergedManager().GetInstanceFor("export.xml", stream);

        Assert.IsType<AbeMedaPlugin>(capability);
    }

    [Fact]
    public void ContentResolution_LeavesUnrelatedXmlToOtherImporters()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\"?><WinCatalog version=\"2024\"><Disk Name=\"D\" /></WinCatalog>"));

        var capability = CreateMergedManager().GetInstanceFor("other.xml", stream);

        Assert.IsType<XmlCatalogPlugin>(capability);
    }

    [Fact]
    public void ContentResolution_SelectsNativeForNativeCatalogExtension()
    {
        using var stream = AbeMedaNativeFixture.CreateCatalog();

        var capability = CreateMergedManager().GetInstanceFor("demo.cdw", stream);

        Assert.IsType<AbeMedaNativePlugin>(capability);
    }
}
