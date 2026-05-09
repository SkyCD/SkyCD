using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Documents;

namespace SkyCD.Plugin.Runtime.Collections;

public sealed class DiscoveredPluginCollection : List<DiscoveredPlugin>
{
    public void Import(
        IReadOnlyCollection<DiscoveredPlugin> discovered,
        IReadOnlyList<PluginDocument> descriptors)
    {
        ArgumentNullException.ThrowIfNull(discovered);
        ArgumentNullException.ThrowIfNull(descriptors);

        Clear();
        var discoveredById = discovered.ToDictionary(static item => item.Id, static item => item, StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in descriptors.Where(static descriptor => descriptor.IsEnabled))
        {
            if (!discoveredById.TryGetValue(descriptor.Id, out var plugin))
            {
                continue;
            }

            Add(plugin);
        }
    }
}
