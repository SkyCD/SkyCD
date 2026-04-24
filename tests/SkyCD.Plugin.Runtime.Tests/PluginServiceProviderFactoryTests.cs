using Microsoft.Extensions.DependencyInjection;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.DependencyInjection;
using SkyCD.Plugin.Runtime.Discovery;

namespace SkyCD.Plugin.Runtime.Tests;

public sealed class PluginServiceProviderFactoryTests
{
    [Fact]
    public void Build_RegistersDiscoveredPluginsAndCapabilities()
    {
        var plugin = new DiscoveredPlugin
        {
            Id = "tests.runtime.di",
            Name = "Runtime Di",
            Version = new Version(1, 2, 3),
            MinHostVersion = new Version(3, 0, 0),
            FileName = "tests.runtime.di.dll",
            Capabilities =
            [
                new StandaloneFileFormatCapability()
            ]
        };

        using var provider = new PluginServiceProviderFactory().Build([plugin]);

        var discovered = provider.GetRequiredService<IReadOnlyList<DiscoveredPlugin>>();
        var byId = provider.GetRequiredService<IReadOnlyDictionary<string, DiscoveredPlugin>>();
        var formatCapabilities = provider.GetServices<IFileFormatPluginCapability>().ToList();
        var keyedFormatCapabilities = provider
            .GetKeyedServices<IFileFormatPluginCapability>(typeof(IFileFormatPluginCapability))
            .ToList();

        Assert.Single(discovered);
        Assert.Same(plugin, discovered[0]);
        Assert.Same(plugin, byId["tests.runtime.di"]);
        Assert.Contains(formatCapabilities, capability => capability is StandaloneFileFormatCapability);
        Assert.Contains(keyedFormatCapabilities, capability => capability is StandaloneFileFormatCapability);
    }
}
