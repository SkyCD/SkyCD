using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using SkyCD.Core.DependencyInjection;
using SkyCD.Plugin.Runtime.Discovery;
using Xunit;

namespace SkyCD.Plugin.Runtime.Tests;

public sealed class ServiceProviderTests
{
    [Fact]
    public void Resolve_RegistersCommonAndHostServices()
    {
        var loggerFactory = ServiceProvider.Resolve(typeof(ILoggerFactory));

        Assert.NotNull(loggerFactory);
    }

    [Fact]
    public void ReregisterPluginsService_RegistersPluginMetadata()
    {
        ServiceProvider.ReregisterPluginsService();
        var list = ServiceProvider.Resolve(typeof(IReadOnlyCollection<DiscoveredPlugin>));
        var byId = ServiceProvider.Resolve(typeof(IReadOnlyDictionary<string, DiscoveredPlugin>));

        Assert.NotNull(list);
        Assert.NotNull(byId);
    }
}

