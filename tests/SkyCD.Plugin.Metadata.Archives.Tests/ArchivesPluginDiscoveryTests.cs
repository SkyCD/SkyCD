using System.Linq;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;

namespace SkyCD.Plugin.Metadata.Archives.Tests;

public sealed class ArchivesPluginDiscoveryTests
{
    [Fact]
    public void Assembly_DiscoversSingleArchivesPluginEntry()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(ArchivesIndexPlugin).Assembly);

        Assert.Equal("SkyCD.Plugin.Metadata.Archives", plugin.Id);
        Assert.IsType<ArchivesIndexPlugin>(Assert.Single(plugin.Capabilities.OfType<IIndexWorkItemProvider>()));
    }
}
