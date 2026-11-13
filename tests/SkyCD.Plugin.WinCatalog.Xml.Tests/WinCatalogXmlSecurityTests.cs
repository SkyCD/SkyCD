using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;
using SkyCD.Plugin.WinCatalog.Xml;
using SkyCD.Plugin.WinCatalog.Xml.Exceptions;
using Xunit;

namespace SkyCD.Plugin.WinCatalog.Xml.Tests;

public sealed class WinCatalogXmlSecurityTests
{
    [Fact]
    public void Parse_DeeplyNestedXml_IsRejectedWithDepthDiagnostic()
    {
        var xml = new StringBuilder();
        xml.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n");
        xml.Append("<WinCatalog version=\"2026\">\r\n");
        xml.Append("<Disk Name=\"Disk 1\">\r\n");
        for (var i = 0; i < 300; i++)
        {
            xml.Append("<Folder Name=\"Deep Level ").Append(i).Append("\">\r\n");
        }

        xml.Append("<File Name=\"deep.txt\" Size=\"1\"/>\r\n");
        for (var i = 0; i < 300; i++)
        {
            xml.Append("</Folder>\r\n");
        }

        xml.Append("</Disk>\r\n");
        xml.Append("</WinCatalog>");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml.ToString()));

        var exception = Assert.Throws<WinCatalogXmlMalformedDocumentException>(
            () => WinCatalogXmlParser.Parse(stream, CancellationToken.None));
        Assert.Contains("maximum supported depth", exception.Message);
    }

    [Fact]
    public void Parse_DeclaringDtd_ThrowsMalformedDocumentException()
    {
        var xml =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
            "<!DOCTYPE WinCatalog [\r\n" +
            "  <!ENTITY xxe SYSTEM \"file:///C:/Windows/win.ini\">\r\n" +
            "]>\r\n" +
            "<WinCatalog version=\"2026\">\r\n" +
            "  <Disk Name=\"Evil Disk\"/>\r\n" +
            "</WinCatalog>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        Assert.Throws<WinCatalogXmlMalformedDocumentException>(
            () => WinCatalogXmlParser.Parse(stream, CancellationToken.None));
    }

    [Fact]
    public void CreateSecureSettings_ProhibitsDtdAndExternalEntities()
    {
        var settings = WinCatalogXmlParser.CreateSecureSettings();

        Assert.Equal(DtdProcessing.Prohibit, settings.DtdProcessing);
        Assert.Equal(WinCatalogXmlParser.MaxCharactersInDocument, settings.MaxCharactersInDocument);
    }

    [Fact]
    public void Parse_CancellationToken_IsHonored()
    {
        var xml =
            "<WinCatalog version=\"2026\">\r\n" +
            "  <Disks>\r\n" +
            "    <Disk Name=\"Disk 1\">\r\n" +
            "      <Folder Name=\"Docs\">\r\n" +
            "        <File Name=\"Doc.txt\" Size=\"100\"/>\r\n" +
            "      </Folder>\r\n" +
            "    </Disk>\r\n" +
            "  </Disks>\r\n" +
            "</WinCatalog>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => WinCatalogXmlParser.Parse(stream, cancellationSource.Token));
    }
}
