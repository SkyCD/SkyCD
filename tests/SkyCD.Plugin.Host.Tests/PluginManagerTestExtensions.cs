using System;
using System.Collections.Generic;
using System.Reflection;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Managers;

namespace SkyCD.Plugin.Host.Tests;

internal static class PluginManagerTestExtensions
{
    private static readonly FieldInfo PluginsField =
        typeof(PluginManager).GetField("_plugins", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PluginManager._plugins field was not found.");

    public static void SetPlugins(this PluginManager pluginManager, IEnumerable<DiscoveredPlugin> discovered)
    {
        if (PluginsField.GetValue(pluginManager) is not List<DiscoveredPlugin> plugins)
        {
            throw new InvalidOperationException("PluginManager._plugins field has unexpected type.");
        }

        plugins.Clear();
        plugins.AddRange(discovered);
    }
}
