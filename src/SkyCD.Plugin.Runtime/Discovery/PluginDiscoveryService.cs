using System.Reflection;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Abstractions.Lifecycle;

namespace SkyCD.Plugin.Runtime.Discovery;

/// <summary>
/// Discovers plugin instances and capabilities from assemblies.
/// </summary>
public sealed class PluginDiscoveryService
{
    private const string EntryPointMetadataKey = "SkyCD.Plugin.EntryPoint";
    private const string IdMetadataKey = "SkyCD.Plugin.Id";
    private const string DisplayNameMetadataKey = "SkyCD.Plugin.DisplayName";
    private const string VersionMetadataKey = "SkyCD.Plugin.Version";
    private const string MinHostVersionMetadataKey = "SkyCD.Plugin.MinHostVersion";
    private const string DescriptionMetadataKey = "SkyCD.Plugin.Description";

    public IReadOnlyList<DiscoveredPlugin> DiscoverFromAssembly(Assembly assembly, Version hostVersion)
    {
        var pluginType = ResolvePluginType(assembly);
        if (pluginType is null)
        {
            return [];
        }

        if (Activator.CreateInstance(pluginType) is not IPlugin pluginInstance)
        {
            return [];
        }

        var assemblyDescriptor = ResolveAssemblyDescriptor(assembly, pluginInstance.Descriptor);
        if (!PluginCompatibilityEvaluator.IsCompatible(assemblyDescriptor, hostVersion))
        {
            return [];
        }

        var wrapped = new AssemblyResolvedPlugin(pluginInstance, assemblyDescriptor);
        return
        [
            new DiscoveredPlugin
            {
                Plugin = wrapped,
                Capabilities = DiscoverCapabilities(pluginInstance)
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

    private static Type? ResolvePluginType(Assembly assembly)
    {
        var entryPointTypeName = GetAssemblyMetadataValue(assembly, EntryPointMetadataKey);
        if (!string.IsNullOrWhiteSpace(entryPointTypeName))
        {
            var entryPointType = assembly.GetType(entryPointTypeName, throwOnError: false, ignoreCase: false);
            if (entryPointType is not null &&
                !entryPointType.IsAbstract &&
                typeof(IPlugin).IsAssignableFrom(entryPointType) &&
                entryPointType.GetConstructor(Type.EmptyTypes) is not null)
            {
                return entryPointType;
            }
        }

        return assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(IPlugin).IsAssignableFrom(type))
            .Where(type => type.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static PluginDescriptor ResolveAssemblyDescriptor(Assembly assembly, PluginDescriptor fallback)
    {
        var assemblyName = assembly.GetName();
        var assemblySimpleName = assemblyName.Name ?? fallback.Id;
        var id = GetAssemblyMetadataValue(assembly, IdMetadataKey) ?? fallback.Id;
        var displayName = GetAssemblyMetadataValue(assembly, DisplayNameMetadataKey)
                          ?? assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title
                          ?? fallback.DisplayName
                          ?? assemblySimpleName;

        var versionText = GetAssemblyMetadataValue(assembly, VersionMetadataKey);
        var version = TryParseVersion(versionText)
                      ?? assemblyName.Version
                      ?? fallback.Version
                      ?? new Version(1, 0, 0);

        var minHostVersionText = GetAssemblyMetadataValue(assembly, MinHostVersionMetadataKey);
        var minHostVersion = TryParseVersion(minHostVersionText)
                             ?? fallback.MinHostVersion
                             ?? new Version(0, 0, 0);

        var description = GetAssemblyMetadataValue(assembly, DescriptionMetadataKey)
                          ?? assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description
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

    private sealed class AssemblyResolvedPlugin(IPlugin inner, PluginDescriptor descriptor) : IPlugin
    {
        public PluginDescriptor Descriptor => descriptor;

        public ValueTask OnLoadAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default) =>
            inner.OnLoadAsync(context, cancellationToken);

        public ValueTask OnInitializeAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default) =>
            inner.OnInitializeAsync(context, cancellationToken);

        public ValueTask OnActivateAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default) =>
            inner.OnActivateAsync(context, cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
