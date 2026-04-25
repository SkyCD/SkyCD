using Microsoft.Extensions.Logging.Abstractions;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Runtime.Managers;

namespace SkyCD.Plugin.Host.Tests;

internal static class PluginManagerTestFactory
{
    public static PluginManager Create()
    {
        return new PluginManager(
            NullLogger<PluginManager>.Instance,
            new AssembliesListFactory(NullLogger.Instance),
            new DiscoveredPluginFactory());
    }
}
