using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Json;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public class JsonCatalogPluginTests
{
    [Fact]
    public void GetOpenAndSaveFormats_ExposesJsonPluginMetadata()
    {
        var service = new FileFormatManager(CreateCatalog().GetCapabilities<IFileFormatPluginCapability>());

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        Assert.Contains(openFormats, format => format.FormatId == "skycd-json" && format.Extensions.Contains(".json"));
        Assert.Contains(saveFormats, format => format.FormatId == "skycd-json" && format.Extensions.Contains(".json"));
    }

    [Fact]
    public async Task WriteAndReadAsync_RoundTripsFixturePayload_WithSchemaEnvelope()
    {
        var service = new FileFormatManager(CreateCatalog().GetCapabilities<IFileFormatPluginCapability>());
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Json", "catalog-v1.json");

        await using var fixtureStream = File.OpenRead(fixturePath);
        var fixtureRead = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-json",
            Source = fixtureStream
        });

        Assert.True(fixtureRead.Success);

        await using var writeStream = new MemoryStream();
        var readPayload = Assert.IsType<JsonElement>(fixtureRead.Payload);
        var writeResult = await service.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-json",
            Target = writeStream,
            Payload = readPayload
        });

        Assert.True(writeResult.Success);

        var writtenText = Encoding.UTF8.GetString(writeStream.ToArray());
        Assert.Contains("\"schemaVersion\":\"skycd.catalog.v1\"", writtenText);
        using var writtenDocument = JsonDocument.Parse(writtenText);
        var writtenPayload = writtenDocument.RootElement.GetProperty("payload");
        Assert.Equal("Žalias katalogas", writtenPayload.GetProperty("title").GetString());

        writeStream.Position = 0;
        var roundTrip = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-json",
            Source = writeStream
        });

        Assert.True(roundTrip.Success);
        var payload = Assert.IsType<JsonElement>(roundTrip.Payload);
        Assert.Equal("Žalias katalogas", payload.GetProperty("title").GetString());
    }

    [Fact]
    public async Task ReadAsync_ReturnsFailure_WhenSchemaVersionMissingOrUnknown()
    {
        var service = new FileFormatManager(CreateCatalog().GetCapabilities<IFileFormatPluginCapability>());
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"payload\":{\"title\":\"x\"}}"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-json",
            Source = stream
        }));

        Assert.Contains("schemaVersion", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static PluginManager CreateCatalog()
    {
        var capability = new JsonCatalogPlugin();
        var catalog = PluginManagerTestFactory.Create();
        catalog.SetPlugins(
        [
            new DiscoveredPlugin
            {
                Id = "tests.json",
                Name = "JsonCatalogPluginTests",
                Version = new Version(1, 0, 0),
                MinHostVersion = new Version(3, 0, 0),
                FileName = "tests.dll",
                Capabilities = [capability]
            }
        ]);
        return catalog;
    }
}
