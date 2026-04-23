using System.Text;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Host;
using SkyCD.Plugin.Host.FileFormats;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Xml;

namespace SkyCD.Plugin.Host.Tests;

public class XmlCatalogPluginTests
{
    [Fact]
    public void GetOpenAndSaveFormats_ExposesXmlPluginMetadata()
    {
        var service = new FileFormatRoutingService(CreateCatalog());

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        Assert.Contains(openFormats, format => format.FormatId == "skycd-xml" && format.Extensions.Contains(".xml"));
        Assert.Contains(saveFormats, format => format.FormatId == "skycd-xml" && format.Extensions.Contains(".xml"));
    }

    [Fact]
    public async Task ReadAndWriteAsync_RoundTripsFixturePayload_WithStableOrdering()
    {
        var service = new FileFormatRoutingService(CreateCatalog());
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Xml", "catalog-v1.xml");

        await using var source = File.OpenRead(fixturePath);
        var readResult = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-xml",
            Source = source
        });

        Assert.True(readResult.Success);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(readResult.Payload);
        Assert.Equal(3, rows.Count);

        await using var target = new MemoryStream();
        var writeResult = await service.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-xml",
            Target = target,
            Payload = rows
        });

        Assert.True(writeResult.Success);
        var xmlText = Encoding.UTF8.GetString(target.ToArray());
        Assert.Contains("xmlns:skycd=\"urn:skycd:catalog\"", xmlText);
        Assert.Contains("schemaVersion=\"1.0\"", xmlText);

        var rootIndex = xmlText.IndexOf("nodeId=\"1\"", StringComparison.Ordinal);
        var childIndex = xmlText.IndexOf("nodeId=\"2\"", StringComparison.Ordinal);
        var fileIndex = xmlText.IndexOf("nodeId=\"3\"", StringComparison.Ordinal);

        Assert.True(rootIndex < childIndex && childIndex < fileIndex);
    }

    [Fact]
    public async Task ReadAsync_RejectsXxePayloads()
    {
        const string xxePayload = """
                                  <?xml version="1.0"?>
                                  <!DOCTYPE foo [ <!ENTITY xxe SYSTEM "file:///etc/passwd"> ]>
                                  <skycd:catalog xmlns:skycd="urn:skycd:catalog" schemaVersion="1.0">
                                    <skycd:node nodeId="1" parentId="" kind="file" name="&xxe;" sizeBytes="1" />
                                  </skycd:catalog>
                                  """;
        var service = new FileFormatRoutingService(CreateCatalog());
        await using var source = new MemoryStream(Encoding.UTF8.GetBytes(xxePayload));

        var exception = await Assert.ThrowsAsync<FileFormatRoutingException>(() => service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-xml",
            Source = source
        }));

        Assert.Contains("DTD", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static PluginCatalog CreateCatalog()
    {
        var plugin = new XmlCatalogPlugin();
        var catalog = new PluginCatalog();
        catalog.SetPlugins(
        [
            new DiscoveredPlugin
            {
                Id = "tests.xml",
                Name = "XmlCatalogPluginTests",
                Version = new Version(1, 0, 0),
                MinHostVersion = new Version(3, 0, 0),
                FileName = "tests.dll",
                Capabilities = [plugin]
            }
        ]);
        return catalog;
    }
}
