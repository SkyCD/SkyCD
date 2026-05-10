using System;
using System.Collections.Generic;
using DryIoc;
using Microsoft.Extensions.Logging;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Runtime.DependencyInjection;
using SkyCD.Plugin.Runtime.DependencyInjection.Registrators;
using SkyCD.Plugin.Runtime.Discovery;
using Xunit;
using PluginContainer = SkyCD.Plugin.Runtime.DependencyInjection.Container;

namespace SkyCD.Plugin.Runtime.Tests;

public sealed class ServiceProviderTests
{
    [Fact]
    public void Constructor_RegistersCommonAndHostServices()
    {
        var provider = PluginContainer.Instance;
        provider.Register(registrator => registrator.Register<SampleService>(Reuse.Singleton));

        var loggerFactory = provider.Resolve(typeof(ILoggerFactory), ifUnresolved: IfUnresolved.ReturnDefault);
        var sample = provider.Resolve(typeof(SampleService), ifUnresolved: IfUnresolved.ReturnDefault);

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

        var provider = PluginContainer.Instance;
        provider.Register(registrator =>
        {
            registrator.RegisterInstance<IReadOnlyList<DiscoveredPlugin>>([plugin]);
            registrator.RegisterInstance<IReadOnlyCollection<DiscoveredPlugin>>([plugin]);
            registrator.RegisterInstance<IReadOnlyDictionary<string, DiscoveredPlugin>>(
                new Dictionary<string, DiscoveredPlugin>(StringComparer.OrdinalIgnoreCase) { [plugin.Id] = plugin });
        });
        provider.Register(registrator => PluginServiceRegistrator.RegisterServices(registrator, plugin));
        var list = provider.Resolve(typeof(IReadOnlyList<DiscoveredPlugin>), ifUnresolved: IfUnresolved.ReturnDefault);
        var byId = provider.Resolve(typeof(IReadOnlyDictionary<string, DiscoveredPlugin>),
            ifUnresolved: IfUnresolved.ReturnDefault);
        var capability = provider.Resolve(typeof(SampleCapability), ifUnresolved: IfUnresolved.ReturnDefault);
        var keyedCapability = provider.Resolve(
            typeof(SampleCapability),
            serviceKey: typeof(SampleCapability),
            ifUnresolved: IfUnresolved.ReturnDefault);

        Assert.NotNull(list);
        Assert.NotNull(byId);
        Assert.NotNull(capability);
        Assert.NotNull(keyedCapability);
        Assert.IsType<SampleCapability>(keyedCapability);
    }

    private sealed class SampleService;

    private sealed class SampleCapability : IPluginCapability;
}