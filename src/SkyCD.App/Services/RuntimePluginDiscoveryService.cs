using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Services;

public sealed class RuntimePluginDiscoveryService
{
    private readonly PluginDiscoveryService discoveryService = new();
    private readonly Version hostVersion = new(3, 0, 0);

    public IReadOnlyList<OptionsPluginItem> Discover(string pluginPath)
    {
        if (string.IsNullOrWhiteSpace(pluginPath) || !Directory.Exists(pluginPath)) return [];

        var discovered = new List<OptionsPluginItem>();
        var seenPluginIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var dllPaths = Directory.GetFiles(pluginPath, "*.dll", SearchOption.AllDirectories);
        foreach (var dllPath in dllPaths) TryDiscoverFromAssembly(dllPath, seenPluginIds, discovered);

        return discovered
            .OrderBy(static plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void TryDiscoverFromAssembly(
        string dllPath,
        ISet<string> seenPluginIds,
        ICollection<OptionsPluginItem> output)
    {
        Assembly assembly;
        try
        {
            assembly = Assembly.LoadFrom(dllPath);
        }
        catch
        {
            return;
        }

        IReadOnlyList<DiscoveredPlugin> plugins;
        try
        {
            plugins = discoveryService.DiscoverFromAssembly(assembly, hostVersion);
        }
        catch
        {
            return;
        }

        foreach (var plugin in plugins)
        {
            var descriptor = plugin.Plugin.Descriptor;
            if (!seenPluginIds.Add(descriptor.Id)) continue;

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
}