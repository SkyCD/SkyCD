using System;
using System.IO;
using System.Linq;
using System.Text;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.Csv;
using SkyCD.Plugin.FileFormat.WinCatalog.Csv;
using SkyCD.Plugin.FileFormat.WinCatalog.Native;
using SkyCD.Plugin.FileFormat.WinCatalog.Xml;
using SkyCD.Plugin.FileFormat.Xml;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.Plugin.FileFormat.WinCatalog.Tests;

public sealed class WinCatalogPluginDiscoveryTests
{
    [Fact]
    public void Assembly_DiscoversOnePluginWithThreeFormatCapabilities()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(WinCatalogCsvPlugin).Assembly);

        Assert.Equal("SkyCD.Plugin.FileFormat.WinCatalog", plugin.Id);
        var formats = plugin.Capabilities
            .OfType<IFileFormatPluginCapability>()
            .Select(capability => capability.SupportedFormat.FormatId)
            .OrderBy(formatId => formatId, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(["wincatalog-csv", "wincatalog-native", "wincatalog-xml"], formats);
    }

    [Fact]
    public void FileFormatManager_ResolvesEachFormatByContent()
    {
        var manager = new FileFormatManager(
        [
            new CsvCatalogPlugin(),
            new XmlCatalogPlugin(),
            new WinCatalogCsvPlugin(),
            new WinCatalogNativePlugin(),
            new WinCatalogXmlPlugin()
        ]);

        using var csv = new MemoryStream(Encoding.UTF8.GetBytes(
            "Disk,Catalog Path,Name,Size\r\n\"Disk 1\",\"Catalog\\Disk 1\\Doc.txt\",\"Doc.txt\",100\r\n"));
        Assert.Equal("wincatalog-csv", manager.GetInstanceFor("export.csv", csv).SupportedFormat.FormatId);

        using var winCatalogXml = new MemoryStream(Encoding.UTF8.GetBytes(
            "<WinCatalog version=\"2026\"><Disks><Disk Name=\"Disk 1\"><File Name=\"Doc.txt\" Size=\"100\"/></Disk></Disks></WinCatalog>"));
        Assert.Equal("wincatalog-xml", manager.GetInstanceFor("export.xml", winCatalogXml).SupportedFormat.FormatId);

        using var skyCdXml = new MemoryStream(Encoding.UTF8.GetBytes(
            "<skycd:catalog xmlns:skycd=\"urn:skycd:catalog\" schemaVersion=\"1.0\"><skycd:node nodeId=\"1\" parentId=\"\" kind=\"folder\" name=\"Root\" sizeBytes=\"0\"/></skycd:catalog>"));
        Assert.Equal("skycd-xml", manager.GetInstanceFor("catalog.xml", skyCdXml).SupportedFormat.FormatId);

        using var native = WinCatalogNativeFixture.CreateCatalog();
        var nativePlugin = new WinCatalogNativePlugin();
        Assert.True(nativePlugin.CanHandleContent(native, "catalog.w3cat"));
        native.Position = 0;
        Assert.Equal("wincatalog-native", manager.GetInstanceFor("catalog.w3cat", native).SupportedFormat.FormatId);

        using var unrelatedSqlite = WinCatalogNativeFixture.CreateUnrelatedSqlite();
        Assert.False(nativePlugin.CanHandleContent(unrelatedSqlite, "catalog.w3cat"));
    }
}
