using Microsoft.Extensions.DependencyInjection;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Factories;

namespace SkyCD.Plugin.Runtime.Tests;

public sealed class ServiceCollectionFactoryTests
{
    [Fact]
    public void BuildPluginServiceCollection_RegistersPluginCapabilities()
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

        var factory = new ServiceCollectionFactory();
        var commonServices = factory.BuildCommonServiceCollection();
        var pluginById = new Dictionary<string, DiscoveredPlugin>(StringComparer.OrdinalIgnoreCase)
        {
            [plugin.Id] = plugin
        };
        commonServices.AddSingleton<IReadOnlyList<DiscoveredPlugin>>([plugin]);
        commonServices.AddSingleton<IReadOnlyCollection<DiscoveredPlugin>>([plugin]);
        commonServices.AddSingleton<IReadOnlyDictionary<string, DiscoveredPlugin>>(pluginById);

        var pluginServices = factory.BuildPluginServiceCollection(plugin);
        foreach (var descriptor in pluginServices)
        {
            commonServices.Add(descriptor);
        }

        using var provider = commonServices.BuildServiceProvider();

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

    [Fact]
    public void BuildCommonServiceCollection_RegistersLoggerFactory()
    {
        var factory = new ServiceCollectionFactory();
        var services = factory.BuildCommonServiceCollection();

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>());
    }
}
