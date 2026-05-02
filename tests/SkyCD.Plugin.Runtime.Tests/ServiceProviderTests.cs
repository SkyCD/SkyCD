using Microsoft.Extensions.DependencyInjection;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Runtime.DependencyInjection;
using SkyCD.Plugin.Runtime.DependencyInjection.Registrators;
using SkyCD.Plugin.Runtime.Discovery;
using PluginServiceProvider = SkyCD.Plugin.Runtime.DependencyInjection.ServiceProvider;

namespace SkyCD.Plugin.Runtime.Tests;

public sealed class ServiceProviderTests
{
    [Fact]
    public void Constructor_RegistersCommonAndHostServices()
    {
        var provider = PluginServiceProvider.Instance;
        IServiceCollection hostServices = new ServiceCollection();
        hostServices.AddSingleton<SampleService>();
        provider.Register(hostServices);

        var loggerFactory = provider.GetService(typeof(Microsoft.Extensions.Logging.ILoggerFactory));
        var sample = provider.GetService(typeof(SampleService));

        Assert.NotNull(loggerFactory);
        Assert.NotNull(sample);
        Assert.IsType<SampleService>(sample);
    }

    [Fact]
    public void Constructor_RegistersPluginMetadataAndCapabilities()
    {
        var plugin = new DiscoveredPlugin
        {
            Id = "tests.runtime.provider",
            Name = "Runtime Provider",
            Version = new Version(1, 0, 0),
            MinHostVersion = new Version(3, 0, 0),
            FileName = "tests.runtime.provider.dll",
            Capabilities = [new SampleCapability()]
        };

        var provider = PluginServiceProvider.Instance;
        IServiceCollection metadata = new ServiceCollection();
        metadata.AddSingleton<IReadOnlyList<DiscoveredPlugin>>([plugin]);
        metadata.AddSingleton<IReadOnlyCollection<DiscoveredPlugin>>([plugin]);
        metadata.AddSingleton<IReadOnlyDictionary<string, DiscoveredPlugin>>(
            new Dictionary<string, DiscoveredPlugin>(StringComparer.OrdinalIgnoreCase)
            {
                [plugin.Id] = plugin
            });
        provider.Register(metadata);
        var pluginServices = new ServiceCollection()
            .AddPluginRegistrator(plugin);
        provider.Register(pluginServices);
        var list = provider.GetService(typeof(IReadOnlyList<DiscoveredPlugin>));
        var byId = provider.GetService(typeof(IReadOnlyDictionary<string, DiscoveredPlugin>));
        var capability = provider.GetService(typeof(SampleCapability));
        var keyedPlugin = provider.GetKeyedService(typeof(DiscoveredPlugin), plugin.Id);

        Assert.NotNull(list);
        Assert.NotNull(byId);
        Assert.NotNull(capability);
        Assert.NotNull(keyedPlugin);
        Assert.IsType<DiscoveredPlugin>(keyedPlugin);
    }

    private sealed class SampleService;
    private sealed class SampleCapability : IPluginCapability;
}
