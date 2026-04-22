using SkyCD.Presentation.ViewModels;
using SkyCD.Plugin.Runtime.Loading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SkyCD.App.Services;

public sealed class RuntimePluginDiscoveryService
{
    private readonly PluginDirectoryDiscoveryService discoveryService = new();
    private readonly Version hostVersion = new(3, 0, 0);

    public IReadOnlyList<OptionsPluginItem> Discover(string pluginPath)
    {
        if (string.IsNullOrWhiteSpace(pluginPath) || !Directory.Exists(pluginPath))
        {
            return [];
        }

        var discovered = new List<OptionsPluginItem>();
        var seenPluginIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var loadResult = discoveryService.Discover([pluginPath], new PluginLoadOptions
        {
            HostVersion = hostVersion,
            EnableAssemblyIsolation = false
        }, fallbackToAssemblyScan: true);

        foreach (var plugin in loadResult.Plugins)
        {
            TryAdd(plugin, seenPluginIds, discovered);
        }

        return discovered
            .OrderBy(static plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void TryAdd(
        SkyCD.Plugin.Runtime.Discovery.DiscoveredPlugin plugin,
        ISet<string> seenPluginIds,
        ICollection<OptionsPluginItem> output)
    {
        var descriptor = plugin.Plugin.Descriptor;
        if (!seenPluginIds.Add(descriptor.Id))
        {
            return;
        }

        var capabilitySummary = plugin.Capabilities.Count == 0
            ? "Generic"
            : string.Join(", ", plugin.Capabilities
                .Select(static capability => capability.GetType().Name)
                .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase));

        var extendedInfo = $"{descriptor.Id} v{descriptor.Version}";
        output.Add(new OptionsPluginItem(
            descriptor.DisplayName,
            capabilitySummary,
            extendedInfo,
            id: descriptor.Id));
    }
}
