using System.Reflection;
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
        var metadata = ResolveAssemblyMetadata(assembly);
        if (metadata is null)
        {
            return [];
        }

        if (!PluginCompatibilityEvaluator.IsCompatible(metadata.Value.MinHostVersion, metadata.Value.MaxHostVersion, hostVersion))
        {
            return [];
        }

        var capabilities = DiscoverCapabilitiesFromAssembly(assembly);
        if (capabilities.Count == 0)
        {
            return [];
        }

        return
        [
            new DiscoveredPlugin
            {
                Id = metadata.Value.Id,
                Name = metadata.Value.Name,
                Version = metadata.Value.Version,
                MinHostVersion = metadata.Value.MinHostVersion,
                MaxHostVersion = metadata.Value.MaxHostVersion,
                Description = metadata.Value.Description,
                FileName = metadata.Value.FileName,
                Capabilities = capabilities
            }
        ];
    }

    private static IReadOnlyList<IPluginCapability> DiscoverCapabilitiesFromAssembly(Assembly assembly)
    {
        return assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(IPluginCapability).IsAssignableFrom(type))
            .Where(type => type.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type =>
            {
                try
                {
                    return Activator.CreateInstance(type) as IPluginCapability;
                }
                catch
                {
                    return null;
                }
            })
            .Where(static capability => capability is not null)
            .Select(static capability => capability!)
            .DistinctBy(static capability => capability.GetType())
            .ToList();
    }

    private static (string Id, string Name, Version Version, Version MinHostVersion, Version? MaxHostVersion, string Description, string FileName)? ResolveAssemblyMetadata(Assembly assembly)
    {
        var assemblyName = assembly.GetName();
        var assemblySimpleName = assemblyName.Name;
        if (string.IsNullOrWhiteSpace(assemblySimpleName))
        {
            return null;
        }

        var id = assembly.GetCustomAttribute<PluginIdAttribute>()?.Id ?? assemblySimpleName;
        var name = assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title ?? assemblySimpleName;
        var version = assemblyName.Version ?? new Version(1, 0, 0, 0);
        var minHostVersion = TryParseVersion(assembly.GetCustomAttribute<MinHostVersionAttribute>()?.Version)
                             ?? new Version(3, 0, 0);
        var maxHostVersion = TryParseVersion(assembly.GetCustomAttribute<MaxHostVersionAttribute>()?.Version);
        var description = assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description ?? string.Empty;
        var fileName = Path.GetFileName(assembly.Location);
        return (id, name, version, minHostVersion, maxHostVersion, description, fileName);
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
