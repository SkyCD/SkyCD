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
        var lifecyclePlugin = CreateLifecyclePlugin(assembly);
        var fallbackDescriptor = lifecyclePlugin?.Descriptor ?? CreateFallbackDescriptor(assembly);
        var assemblyDescriptor = ResolveAssemblyDescriptor(assembly, fallbackDescriptor);
        if (!PluginCompatibilityEvaluator.IsCompatible(assemblyDescriptor, hostVersion))
        {
            return [];
        }

        IPlugin plugin = lifecyclePlugin is null
            ? new AssemblyLifecyclePlugin(assemblyDescriptor)
            : new AssemblyResolvedPlugin(lifecyclePlugin, assemblyDescriptor);

        return
        [
            new DiscoveredPlugin
            {
                Plugin = plugin,
                Capabilities = DiscoverCapabilitiesFromAssembly(assembly, assemblyDescriptor.Id)
            }
        ];
    }

    public IReadOnlyList<DiscoveredPlugin> DiscoverFromPlugins(IEnumerable<IPlugin> plugins)
    {
        return plugins
            .Where(static plugin => plugin is not null)
            .GroupBy(static plugin => plugin.GetType().Assembly)
            .Select(static group => group
                .OrderBy(static plugin => plugin.GetType().FullName, StringComparer.Ordinal)
                .First())
            .Select(static plugin => new DiscoveredPlugin
            {
                Plugin = new AssemblyResolvedPlugin(
                    plugin,
                    ResolveAssemblyDescriptor(plugin.GetType().Assembly, plugin.Descriptor)),
                Capabilities = DiscoverCapabilities(plugin)
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

    private static IPlugin? CreateLifecyclePlugin(Assembly assembly)
    {
        var pluginType = assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(IPlugin).IsAssignableFrom(type))
            .Where(type => type.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .FirstOrDefault();

        if (pluginType is null)
        {
            return null;
        }

        return Activator.CreateInstance(pluginType) as IPlugin;
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

    private static PluginDescriptor CreateFallbackDescriptor(Assembly assembly)
    {
        var assemblyName = assembly.GetName();
        var assemblySimpleName = assemblyName.Name ?? "plugin";
        return new PluginDescriptor(
            assemblySimpleName.ToLowerInvariant(),
            assemblySimpleName,
            ResolveReleaseVersion(assembly) ?? assemblyName.Version ?? new Version(1, 0, 0),
            new Version(0, 0, 0));
    }

    private static PluginDescriptor ResolveAssemblyDescriptor(Assembly assembly, PluginDescriptor fallback)
    {
        var assemblyName = assembly.GetName();
        var assemblySimpleName = assemblyName.Name ?? fallback.Id;
        var id = GetAssemblyMetadataValue(assembly, IdMetadataKey) ?? fallback.Id;
        var displayName = assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title
                          ?? assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product
                          ?? fallback.DisplayName
                          ?? assemblySimpleName;

        var version = ResolveReleaseVersion(assembly)
                      ?? assemblyName.Version
                      ?? fallback.Version
                      ?? new Version(1, 0, 0);

        var minHostVersionText = GetAssemblyMetadataValue(assembly, MinHostVersionMetadataKey);
        var minHostVersion = TryParseVersion(minHostVersionText)
                             ?? fallback.MinHostVersion
                             ?? new Version(0, 0, 0);

        var description = assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description
                          ?? fallback.Description;

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

    private sealed class AssemblyResolvedPlugin : IPlugin
    {
        public AssemblyResolvedPlugin(IPlugin inner, PluginDescriptor descriptor)
        {
            _ = inner;
            Descriptor = descriptor;
        }

        public PluginDescriptor Descriptor { get; }
    }

    private sealed class AssemblyLifecyclePlugin(PluginDescriptor descriptor) : IPlugin
    {
        public PluginDescriptor Descriptor => descriptor;
    }
}
