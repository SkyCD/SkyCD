using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Abstractions.Lifecycle;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Documents;

namespace SkyCD.Plugin.Runtime.Factories;

public sealed class DiscoveredPluginFactory
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
            Author = string.IsNullOrWhiteSpace(metadata.AuthorName)
                ? null
                : new PluginAuthorDocument { Name = metadata.AuthorName, Url = metadata.AuthorUrl },
            ProjectUrl = metadata.ProjectUrl,
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

    private static (string Id, string Name, string AuthorName, string? AuthorUrl, string? ProjectUrl, System.Version Version, System.Version MinHostVersion, System.Version? MaxHostVersion, string Description, string FileName)? ResolveAssemblyMetadata(Assembly assembly)
    {
        var assemblyName = assembly.GetName();
        var assemblySimpleName = assemblyName.Name;
        if (string.IsNullOrWhiteSpace(assemblySimpleName))
        {
            return null;
        }

        var id = assembly.GetCustomAttribute<PluginIdAttribute>()?.Id ?? assemblySimpleName;
        var name = assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title ?? assemblySimpleName;
        var authorName = NormalizeAuthor(assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company, name, id);
        var authorUrl = ResolveAuthorUrl(assembly);
        var projectUrl = ResolveProjectUrl(assembly);
        var version = assemblyName.Version ?? new Version(1, 0, 0, 0);
        var minHostVersion = TryParseVersion(assembly.GetCustomAttribute<MinHostVersionAttribute>()?.Version)
                             ?? new Version(3, 0, 0);
        var maxHostVersion = TryParseVersion(assembly.GetCustomAttribute<MaxHostVersionAttribute>()?.Version);
        var description = assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description ?? string.Empty;
        var fileName = Path.GetFileName(assembly.Location);
        return (id, name, authorName, authorUrl, projectUrl, version, minHostVersion, maxHostVersion, description, fileName);
    }

    private static Version? TryParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Version.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string NormalizeAuthor(string? candidate, string pluginName, string pluginId)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return "Unknown author";
        }

        if (string.Equals(candidate, pluginName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate, pluginId, StringComparison.OrdinalIgnoreCase))
        {
            return "Unknown author";
        }

        return candidate;
    }

    private static string? ResolveAuthorUrl(Assembly assembly)
    {
        var attributes = assembly.GetCustomAttributes<AssemblyMetadataAttribute>();
        var rawValue =
            attributes.FirstOrDefault(static item => string.Equals(item.Key, "AuthorUrl", StringComparison.OrdinalIgnoreCase))?.Value
            ?? attributes.FirstOrDefault(static item => string.Equals(item.Key, "PluginAuthorUrl", StringComparison.OrdinalIgnoreCase))?.Value;

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        return Uri.TryCreate(rawValue, UriKind.Absolute, out _) ? rawValue : null;
    }

    private static string? ResolveProjectUrl(Assembly assembly)
    {
        var attributes = assembly.GetCustomAttributes<AssemblyMetadataAttribute>();
        var rawValue =
            attributes.FirstOrDefault(static item => string.Equals(item.Key, "PackageProjectUrl", StringComparison.OrdinalIgnoreCase))?.Value
            ?? attributes.FirstOrDefault(static item => string.Equals(item.Key, "ProjectUrl", StringComparison.OrdinalIgnoreCase))?.Value
            ?? attributes.FirstOrDefault(static item => string.Equals(item.Key, "RepositoryUrl", StringComparison.OrdinalIgnoreCase))?.Value;

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        return Uri.TryCreate(rawValue, UriKind.Absolute, out _) ? rawValue : null;
    }
}
