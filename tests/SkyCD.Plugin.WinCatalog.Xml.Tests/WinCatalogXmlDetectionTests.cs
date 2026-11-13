using System;
using System.IO;
using System.Text;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Plugin.WinCatalog.Xml;
using Xunit;

namespace SkyCD.Plugin.WinCatalog.Xml.Tests;

public sealed class WinCatalogXmlDetectionTests
{
    private readonly WinCatalogXmlPlugin plugin = new();

    [Fact]
    public void SupportedFormat_HasExpectedMetadata()
    {
        var format = plugin.SupportedFormat;
        Assert.Equal("wincatalog-xml", format.FormatId);
        Assert.Equal("WinCatalog XML Report", format.DisplayName);
        Assert.Contains(".xml", format.Extensions);
        Assert.True(format.CanRead);
        Assert.False(format.CanWrite);
    }

    [Fact]
    public void CanHandleContent_ReturnsTrueForNestedWinCatalogXml()
    {
        var winCatalogXml =
            "<WinCatalog version=\"2026\">\r\n" +
            "  <Disks>\r\n" +
            "    <Disk Name=\"Disk 1\">\r\n" +
            "      <Folder Name=\"Docs\">\r\n" +
            "        <File Name=\"Doc.txt\" Size=\"100\" Modified=\"2026-01-01 10:00:00\"/>\r\n" +
            "      </Folder>\r\n" +
            "    </Disk>\r\n" +
            "  </Disks>\r\n" +
            "</WinCatalog>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(winCatalogXml));

        var result = plugin.CanHandleContent(stream, "export.xml");
        Assert.True(result);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_ReturnsTrueForFlatSearchResults()
    {
        var winCatalogXml =
            "<WinCatalog version=\"2026\">\r\n" +
            "  <Results>\r\n" +
            "    <Item Disk=\"Disk 1\" CatalogPath=\"Catalog\\Disk 1\\Doc.txt\" Name=\"Doc.txt\" Size=\"100\" Type=\"File\"/>\r\n" +
            "  </Results>\r\n" +
            "</WinCatalog>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(winCatalogXml));

        Assert.True(plugin.CanHandleContent(stream, "search-results.xml"));
    }

    [Fact]
    public void CanHandleContent_ReturnsFalseForSkyCdNativeXml()
    {
        var skyCdXml =
            "<skycd:catalog xmlns:skycd=\"urn:skycd:catalog\" schemaVersion=\"1.0\">\r\n" +
            "  <skycd:node nodeId=\"1\" parentId=\"\" kind=\"folder\" name=\"Root\" sizeBytes=\"0\"/>\r\n" +
            "</skycd:catalog>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(skyCdXml));

        Assert.False(plugin.CanHandleContent(stream, "catalog.xml"));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_ReturnsFalseForGenericXml()
    {
        var genericXml =
            "<configuration>\r\n" +
            "  <appSettings>\r\n" +
            "    <add key=\"someKey\" value=\"someValue\"/>\r\n" +
            "  </appSettings>\r\n" +
            "</configuration>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(genericXml));

        Assert.False(plugin.CanHandleContent(stream, "app.config"));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_ReturnsFalseForMalformedXml()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<WinCatalog><Disks>"));

        Assert.False(plugin.CanHandleContent(stream, "broken.xml"));
    }

    [Fact]
    public void FileFormatManager_ResolvesWinCatalogXmlWhenContentMatches()
    {
        var providers = new IFileFormatPluginCapability[]
        {
            new SkyCD.Plugin.Xml.XmlCatalogPlugin(),
            new WinCatalogXmlPlugin()
        };
        var manager = new FileFormatManager(providers);

        var skyCdXml =
            "<skycd:catalog xmlns:skycd=\"urn:skycd:catalog\" schemaVersion=\"1.0\">\r\n" +
            "  <skycd:node nodeId=\"1\" parentId=\"\" kind=\"folder\" name=\"Root\" sizeBytes=\"0\"/>\r\n" +
            "</skycd:catalog>";
        using var skyCdStream = new MemoryStream(Encoding.UTF8.GetBytes(skyCdXml));
        Assert.Equal("skycd-xml", manager.GetInstanceFor("catalog.xml", skyCdStream).SupportedFormat.FormatId);

        var winCatalogXml =
            "<WinCatalog version=\"2026\">\r\n" +
            "  <Disks>\r\n" +
            "    <Disk Name=\"Disk 1\">\r\n" +
            "      <File Name=\"Doc.txt\" Size=\"100\"/>\r\n" +
            "    </Disk>\r\n" +
            "  </Disks>\r\n" +
            "</WinCatalog>";
        using var winCatalogStream = new MemoryStream(Encoding.UTF8.GetBytes(winCatalogXml));
        Assert.Equal("wincatalog-xml", manager.GetInstanceFor("export.xml", winCatalogStream).SupportedFormat.FormatId);
    }
}
