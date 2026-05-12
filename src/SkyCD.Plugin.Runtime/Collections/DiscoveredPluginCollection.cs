using System;
using System.Collections.Generic;
using System.Linq;
using DryIoc;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Documents;

namespace SkyCD.Plugin.Runtime.Collections;

public sealed class DiscoveredPluginCollection : List<DiscoveredPlugin>
{
    public void RegisterPluginServices(IRegistrator registrator)
    {
        ArgumentNullException.ThrowIfNull(registrator);

        foreach (var plugin in this)
        {
            plugin.RegisterPluginServices(registrator);
        }
    }

    public void Import(
        IReadOnlyCollection<DiscoveredPlugin> discovered,
        IReadOnlyList<PluginDocument> descriptors)
    {
        ArgumentNullException.ThrowIfNull(discovered);
        ArgumentNullException.ThrowIfNull(descriptors);

        Clear();
        var descriptorsById = descriptors
            .Where(static descriptor => !string.IsNullOrWhiteSpace(descriptor.Id))
            .ToDictionary(static descriptor => descriptor.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var plugin in discovered)
        {
            if (!descriptorsById.TryGetValue(plugin.Id, out var descriptor))
            {
                Add(plugin);
                continue;
            }

            if (descriptor.IsEnabled && descriptor.IsAvailable)
            {
                Add(plugin);
            }
        }
    }
}
