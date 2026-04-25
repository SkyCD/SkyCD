using System.Reflection;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Abstractions.Lifecycle;
using SkyCD.Plugin.Runtime.Discovery;

namespace SkyCD.Plugin.Runtime.Factories;

internal sealed class DiscoveredPluginFactory
{
    public DiscoveredPlugin BuildFromAssembly(Assembly assembly)
    {
        var metadata = ResolveAssemblyMetadata(assembly)
                       ?? throw new InvalidOperationException($"Assembly '{assembly.FullName}' does not provide a valid plugin identity.");

        var capabilities = DiscoverCapabilitiesFromAssembly(assembly);
        if (capabilities.Count == 0)
        {
            throw new InvalidOperationException($"Assembly '{assembly.FullName}' does not expose plugin capabilities.");
        }

        return new DiscoveredPlugin
        {
            Id = metadata.Id,
            Name = metadata.Name,
            Version = metadata.Version,
            MinHostVersion = metadata.MinHostVersion,
            MaxHostVersion = metadata.MaxHostVersion,
            Description = metadata.Description,
            FileName = metadata.FileName,
            Capabilities = capabilities
        };
    }

    private static IReadOnlyList<IPluginCapability> DiscoverCapabilitiesFromAssembly(Assembly assembly)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Failed to inspect assembly '{assembly.FullName}'.", exception);
        }

        return types
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
