using System;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Documents;

namespace SkyCD.Plugin.Runtime.Factories;

public sealed class PluginDocumentFactory
{
    public PluginDocument Create(DiscoveredPlugin plugin, string assemblyPath, DateTimeOffset discoveredAt)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);

        return new PluginDocument
        {
            Id = plugin.Id,
            Name = plugin.Name,
            Author = plugin.Author,
            ProjectUrl = plugin.ProjectUrl,
            Version = plugin.Version.ToString(),
            Constraints = new PluginConstraintsDocument
            {
                MinHostVersion = plugin.MinHostVersion.ToString(),
                MaxHostVersion = plugin.MaxHostVersion?.ToString()
            },
            Description = plugin.Description,
            AssemblyPath = assemblyPath,
            IsEnabled = true,
            IsAvailable = true,
            LastDiscoveredAt = discoveredAt
        };
    }
}
