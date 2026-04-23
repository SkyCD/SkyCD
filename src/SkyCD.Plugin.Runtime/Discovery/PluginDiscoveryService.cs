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
    private const string IdMetadataKey = "SkyCD.Plugin.Id";
    private const string MinHostVersionMetadataKey = "SkyCD.Plugin.MinHostVersion";

    public IReadOnlyList<DiscoveredPlugin> DiscoverFromAssembly(Assembly assembly, Version hostVersion)
    {
        var assemblyDescriptor = ResolveAssemblyDescriptor(assembly);
        if (assemblyDescriptor is null || !PluginCompatibilityEvaluator.IsCompatible(assemblyDescriptor, hostVersion))
        {
            return [];
        }

        return
        [
            new DiscoveredPlugin
            {
                Plugin = new AssemblyLifecyclePlugin(assemblyDescriptor),
                Capabilities = DiscoverCapabilitiesFromAssembly(assembly, assemblyDescriptor.Id)
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
                var descriptor = ResolveAssemblyDescriptor(plugin.GetType().Assembly) ?? plugin.Descriptor;
                return new DiscoveredPlugin
                {
                    Plugin = new AssemblyLifecyclePlugin(descriptor),
                    Capabilities = DiscoverCapabilities(plugin)
                };
            })
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

    private static IReadOnlyList<IPluginCapability> DiscoverCapabilitiesFromAssembly(Assembly assembly, string pluginId)
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
        return provider.GetKeyedServices<IPluginCapability>(pluginId)
            .DistinctBy(static capability => capability.GetType())
            .ToList();
    }

    private static PluginDescriptor? ResolveAssemblyDescriptor(Assembly assembly)
    {
        var id = GetAssemblyMetadataValue(assembly, IdMetadataKey);
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var minHostVersionText = GetAssemblyMetadataValue(assembly, MinHostVersionMetadataKey);
        var minHostVersion = TryParseVersion(minHostVersionText);
        if (minHostVersion is null)
        {
            return null;
        }

        var assemblyName = assembly.GetName();
        var assemblySimpleName = assemblyName.Name ?? id;
        var displayName = assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title
                          ?? assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product
                          ?? assemblySimpleName;

        var version = ResolveReleaseVersion(assembly)
                      ?? assemblyName.Version
                      ?? new Version(1, 0, 0);

        var description = assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description
                          ?? string.Empty;

        return new PluginDescriptor(id, displayName, version, minHostVersion, description);
    }

    private static string? GetAssemblyMetadataValue(Assembly assembly, string key)
    {
        return assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    private static Version? TryParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Version.TryParse(value, out var parsed) ? parsed : null;
    }

    private static Version? ResolveReleaseVersion(Assembly assembly)
    {
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return null;
        }

        var value = informationalVersion.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        var plusIndex = value.IndexOf('+');
        if (plusIndex >= 0)
        {
            value = value[..plusIndex];
        }

        var dashIndex = value.IndexOf('-');
        if (dashIndex >= 0)
        {
            value = value[..dashIndex];
        }

        return TryParseVersion(value);
    }

    private sealed class AssemblyLifecyclePlugin(PluginDescriptor descriptor) : IPlugin
    {
        public PluginDescriptor Descriptor => descriptor;
    }
}
