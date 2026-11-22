using System.Linq;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;

namespace SkyCD.Plugin.Metadata.DiskImages.Tests;

public sealed class DiskImagesPluginDiscoveryTests
{
    [Fact]
    public void Assembly_DiscoversSingleDiskImagesPluginEntry()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(DiskImagesIndexPlugin).Assembly);

        Assert.Equal("skycd.diskimages", plugin.Id);
        Assert.IsType<DiskImagesIndexPlugin>(Assert.Single(plugin.Capabilities.OfType<IIndexWorkItemProvider>()));
        Assert.Empty(plugin.Capabilities.OfType<IFileFormatPluginCapability>());
    }
}
