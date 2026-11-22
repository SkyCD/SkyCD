using System;
using System.IO;
using System.Linq;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Iso;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public sealed class IsoImageIndexPluginTests
{
    [Fact]
    public void AssemblyDiscovery_RegistersWorkItemProvider_WithoutFileFormatCapability()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(IsoImageIndexPlugin).Assembly);

        Assert.Single(plugin.Capabilities.OfType<IIndexWorkItemProvider>());
        Assert.Empty(plugin.Capabilities.OfType<IFileFormatPluginCapability>());
    }

    internal static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Iso", fileName);
}
