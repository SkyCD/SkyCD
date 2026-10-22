using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using SkyCD.Plugin.Abstractions.Localization;
using SkyCD.Plugin.Runtime.Discovery;

namespace SkyCD.App;

/// <summary>Synchronizes enabled plugin catalogs while isolating provider failures.</summary>
public sealed class PluginTranslationRegistry(ILocalizationService localizer, ILogger<PluginTranslationRegistry> logger)
{
    private readonly HashSet<string> registered = new(StringComparer.Ordinal);

    public void Refresh(IEnumerable<DiscoveredPlugin> plugins)
    {
        var active = new HashSet<string>(StringComparer.Ordinal);
        foreach (var plugin in plugins)
        {
            var providers = plugin.Capabilities.OfType<ILocalizationPluginCapability>().ToArray();
            if (providers.Length == 0)
            {
                continue;
            }

            var namespaceName = "plugins." + plugin.Id;
            active.Add(namespaceName);
            try
            {
                localizer.Register(namespaceName, new PluginTranslationProvider(providers));
                registered.Add(namespaceName);
            }
            catch (Exception exception)
            {
                // Plugin code is an isolation boundary: keep the last good catalog on refresh.
                logger.LogWarning(exception, "Could not load translations for plugin '{PluginId}'.", plugin.Id);
            }
        }

        foreach (var removed in registered.Except(active).ToArray())
        {
            localizer.Unregister(removed);
            registered.Remove(removed);
        }
    }
}
