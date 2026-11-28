using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Configuration;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;
using SkyCD.Plugin.Metadata.Xmp.Models;

namespace SkyCD.Plugin.Metadata.Xmp.Tests;

/// <summary>
/// End-to-end coverage inside the host pipeline: which sources the sidecar capability claims, metadata
/// selection, and discovery without a manifest.
/// </summary>
public sealed class XmpPluginIntegrationTests
{
    private const string PluginId = "SkyCD.Plugin.Xmp";

    [Fact]
    public void Host_ClaimsOnlySidecarSources()
    {
        var manager = CreateManager();

        Assert.Equal([XmpSchema.SchemaId],
            manager.ResolveProviders(new MediaItem(".xmp", null, 10)).Select(identity => identity.SchemaId));
        Assert.Equal([XmpSchema.SchemaId],
            manager.ResolveProviders(new MediaItem(string.Empty, "application/rdf+xml", 10))
                .Select(identity => identity.SchemaId));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".jpg", null, 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".tif", null, 10)));
    }

    [Fact]
    public async Task StandardMetadataPass_ReturnsSidecarValues()
    {
        var data = Encoding.UTF8.GetBytes(XmpFixtures.Xmp(keywords: ["sunset"], rating: 4));
        var manager = CreateManager();

        var results = await manager.ProcessAsync(
            new MediaItem(".xmp", null, data.Length),
            Open(data),
            selection: MediaProviderSelection.StandardMetadata);

        var result = Assert.Single(results);
        Assert.False(result.Failed);
        Assert.Contains(result.Metadata!.Values, value => value.Key == XmpSchema.Tags);
        Assert.Contains(result.Metadata.Values, value => value.Key == XmpSchema.Rating);
    }

    [Fact]
    public async Task OtherSources_AreNotRead()
    {
        var data = Encoding.UTF8.GetBytes(XmpFixtures.Xmp(keywords: ["sunset"]));
        var manager = CreateManager();

        var results = await manager.ProcessAsync(
            new MediaItem(".jpg", null, data.Length),
            Open(data),
            selection: MediaProviderSelection.StandardMetadata);

        Assert.Empty(results);
    }

    [Fact]
    public void AssemblyDiscovery_FindsTheCapabilityWithoutAManifest()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(XmpMetadataPlugin).Assembly);

        Assert.Equal(PluginId, plugin.Id);
        Assert.Equal("XMP Sidecar Metadata", plugin.Name);
        Assert.Equal(XmpSchema.SchemaVersion, plugin.Capabilities.OfType<IMediaMetadataCapability>()
            .Single().Descriptor.Schema.Version);
        Assert.DoesNotContain(plugin.Capabilities, capability => capability is IPluginConfigurationCapability);
    }

    private static MediaExtensionManager CreateManager() =>
        new([new MediaProvider(PluginId, "1.0.0", new XmpMetadataPlugin())]);

    private static Func<CancellationToken, Task<Stream>> Open(byte[] data) =>
        _ => Task.FromResult<Stream>(new MemoryStream(data, writable: false));
}
