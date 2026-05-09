using System;
using System.Collections.Generic;
using DryIoc;
using Microsoft.Extensions.Logging;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Runtime.DependencyInjection;
using SkyCD.Plugin.Runtime.Discovery;
using Xunit;
using PluginServiceProvider = SkyCD.Plugin.Runtime.DependencyInjection.ServiceProvider;

namespace SkyCD.Plugin.Runtime.Tests;

public sealed class ServiceProviderTests
{
    [Fact]
    public void Constructor_RegistersCommonAndHostServices()
    {
        var provider = PluginServiceProvider.Instance;
        provider.Register(registrator => registrator.Register<SampleService>(Reuse.Singleton));

        var loggerFactory = provider.GetService(typeof(ILoggerFactory));
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
        provider.Register(registrator =>
        {
            registrator.RegisterInstance<IReadOnlyList<DiscoveredPlugin>>([plugin]);
            registrator.RegisterInstance<IReadOnlyCollection<DiscoveredPlugin>>([plugin]);
            registrator.RegisterInstance<IReadOnlyDictionary<string, DiscoveredPlugin>>(
                new Dictionary<string, DiscoveredPlugin>(StringComparer.OrdinalIgnoreCase) { [plugin.Id] = plugin });
        });
        provider.Register(registrator => registrator.AddPluginRegistrator(plugin));
        var list = provider.GetService(typeof(IReadOnlyList<DiscoveredPlugin>));
        var byId = provider.GetService(typeof(IReadOnlyDictionary<string, DiscoveredPlugin>));
        var capability = provider.GetService(typeof(SampleCapability));
        var keyedCapability = provider.GetKeyedService(typeof(SampleCapability), typeof(SampleCapability));

        Assert.NotNull(list);
        Assert.NotNull(byId);
        Assert.NotNull(capability);
        Assert.NotNull(keyedCapability);
        Assert.IsType<SampleCapability>(keyedCapability);
    }

    private sealed class SampleService;
    private sealed class SampleCapability : IPluginCapability;
}
