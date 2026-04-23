using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Abstractions.Lifecycle;

namespace SkyCD.Plugin.Runtime.Discovery;

/// <summary>
/// Discovers plugin instances and capabilities from assemblies.
/// </summary>
public sealed class PluginDiscoveryService
{
    public IReadOnlyList<DiscoveredPlugin> DiscoverFromAssembly(Assembly assembly, Version hostVersion)
    {
        var plugin = ResolvePluginInstance(assembly);
        if (plugin is null)
        {
            return [];
        }

        var metadata = ResolveAssemblyMetadata(assembly, plugin);
        if (metadata is null || !PluginCompatibilityEvaluator.IsCompatible(metadata.Value.MinHostVersion, metadata.Value.MaxHostVersion, hostVersion))
        {
            return [];
        }

        return
        [
            new DiscoveredPlugin
            {
                Plugin = plugin,
                Capabilities = GetServicesFromAssembly(assembly, metadata.Value.Id)
            }
        ];
    }

    public IReadOnlyList<DiscoveredPlugin> DiscoverFromPlugins(IEnumerable<IPlugin> plugins)
    {
        return plugins
            .Where(plugin => plugin is not null)
            .GroupBy(plugin => plugin.GetType().Assembly)
            .Select(group => group
                .OrderBy(plugin => plugin.GetType().FullName, StringComparer.Ordinal)
                .First())
            .Select(plugin =>
            {
                var metadata = ResolveAssemblyMetadata(plugin.GetType().Assembly, plugin);
                if (metadata is null)
                {
                    return null;
                }

                return new DiscoveredPlugin
                {
                    Plugin = plugin,
                    Capabilities = DiscoverCapabilities(plugin)
                };
            })
            .Where(static discovered => discovered is not null)
            .Select(static discovered => discovered!)
            .ToList();
    }

    private static IReadOnlyList<IPluginCapability> DiscoverCapabilities(IPlugin pluginInstance)
    {
        var pluginType = pluginInstance.GetType();
        var capabilityInterfaces = pluginType.GetInterfaces()
            .Where(@interface => @interface != typeof(IPluginCapability))
            .Where(@interface => typeof(IPluginCapability).IsAssignableFrom(@interface))
            .Distinct()
            .ToList();

        var capabilities = new List<IPluginCapability>();
        foreach (var capabilityType in capabilityInterfaces)
        {
            if (pluginInstance is IPluginCapability capability &&
                capabilityType.IsInstanceOfType(capability) &&
                capabilities.All(existing => !ReferenceEquals(existing, capability)))
            {
                capabilities.Add(capability);
            }
        }

        return capabilities;
    }

    private static IPlugin? ResolvePluginInstance(Assembly assembly)
    {
        var pluginType = assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(IPlugin).IsAssignableFrom(type))
            .Where(type => type.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .FirstOrDefault();

        return pluginType is null ? null : Activator.CreateInstance(pluginType) as IPlugin;
    }

    private static IReadOnlyList<IPluginCapability> GetServicesFromAssembly(Assembly assembly, string pluginId)
    {
        var services = new ServiceCollection();
        var capabilityTypes = assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(IPluginCapability).IsAssignableFrom(type))
            .Where(type => type.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        foreach (var capabilityType in capabilityTypes)
        {
            if (Activator.CreateInstance(capabilityType) is not IPluginCapability capability)
            {
                continue;
            }

            services.AddKeyedSingleton(typeof(IPluginCapability), pluginId, capability);
            foreach (var serviceType in capabilityType.GetInterfaces()
                         .Where(@interface => @interface != typeof(IPluginCapability))
                         .Where(@interface => typeof(IPluginCapability).IsAssignableFrom(@interface))
                         .Distinct())
            {
                services.AddKeyedSingleton(serviceType, pluginId, capability);
            }
        }

        using var provider = services.BuildServiceProvider();
        var discoveredServices = provider.GetKeyedServices<IPluginCapability>(pluginId)
            .DistinctBy(static capability => capability.GetType())
            .ToList();
        return discoveredServices;
    }

    private static (string Id, Version MinHostVersion, Version? MaxHostVersion)? ResolveAssemblyMetadata(Assembly assembly, IPlugin plugin)
    {
        var assemblyName = assembly.GetName();
        var assemblySimpleName = assemblyName.Name;
        if (string.IsNullOrWhiteSpace(assemblySimpleName))
        {
            return null;
        }

        var id = assembly.GetCustomAttribute<PluginIdAttribute>()?.Id ?? plugin.Id;
        var minHostVersion = TryParseVersion(assembly.GetCustomAttribute<MinHostVersionAttribute>()?.Version)
                             ?? plugin.MinHostVersion;
        var maxHostVersion = TryParseVersion(assembly.GetCustomAttribute<MaxHostVersionAttribute>()?.Version);
        return (id, minHostVersion, maxHostVersion ?? plugin.MaxHostVersion);
    }

    private static Version? TryParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Version.TryParse(value, out var parsed) ? parsed : null;
    }

}
