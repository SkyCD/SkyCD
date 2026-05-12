using System;
using System.Collections.Generic;
using DryIoc;
using Microsoft.Extensions.Logging;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Runtime.DependencyInjection;
using SkyCD.Plugin.Runtime.DependencyInjection.Registrators;
using SkyCD.Plugin.Runtime.Discovery;
using Xunit;

namespace SkyCD.Plugin.Runtime.Tests;

public sealed class ServiceProviderTests
{
    [Fact]
    public void RebuildGlobal_RegistersCommonAndHostServices()
    {
        ServiceProvider.RebuildGlobal();

        var loggerFactory = ServiceProvider.Resolve(typeof(ILoggerFactory));

        Assert.NotNull(loggerFactory);
    }

    [Fact]
    public void CreateSubcontainer_RegistersPluginMetadataAndCapabilities()
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

        ServiceProvider.RebuildGlobal();
        var provider = PluginServiceRegistrator.CreatePluginSubcontainer([plugin]);
        var list = provider.Resolve(typeof(IReadOnlyCollection<DiscoveredPlugin>), ifUnresolved: IfUnresolved.ReturnDefault);
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

    private sealed class SampleCapability : IPluginCapability;
}
