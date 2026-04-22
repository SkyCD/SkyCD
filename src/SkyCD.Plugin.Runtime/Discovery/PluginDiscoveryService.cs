using System.Reflection;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Abstractions.Lifecycle;

namespace SkyCD.Plugin.Runtime.Discovery;

/// <summary>
///     Discovers plugin instances and capabilities from assemblies.
/// </summary>
public sealed class PluginDiscoveryService
{
    public IReadOnlyList<DiscoveredPlugin> DiscoverFromAssembly(Assembly assembly, Version hostVersion)
    {
        var plugins = new List<DiscoveredPlugin>();

        var pluginTypes = assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(IPlugin).IsAssignableFrom(type))
            .Where(type => type.GetConstructor(Type.EmptyTypes) is not null);

        foreach (var pluginType in pluginTypes)
        {
            if (Activator.CreateInstance(pluginType) is not IPlugin pluginInstance) continue;

            if (!PluginCompatibilityEvaluator.IsCompatible(pluginInstance.Descriptor, hostVersion)) continue;

            var capabilityInterfaces = pluginType.GetInterfaces()
                .Where(@interface => @interface != typeof(IPluginCapability))
                .Where(@interface => typeof(IPluginCapability).IsAssignableFrom(@interface))
                .Distinct()
                .ToList();

            var capabilities = new List<IPluginCapability>();
            foreach (var capabilityType in capabilityInterfaces)
                if (pluginInstance is IPluginCapability capability &&
                    capabilityType.IsInstanceOfType(capability))
                    capabilities.Add(capability);

            plugins.Add(new DiscoveredPlugin
            {
                Plugin = pluginInstance,
                Capabilities = capabilities
            });
        }

        return plugins;
    }
}