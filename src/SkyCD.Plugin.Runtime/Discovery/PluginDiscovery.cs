using System.Collections.Generic;
using SkyCD.Plugin.Runtime.Collections;
using SkyCD.Plugin.Runtime.Loading;

namespace SkyCD.Plugin.Runtime.Discovery;

/// <summary>
/// A completed discovery walk that has not touched live plugin state yet. Building it is the expensive
/// part and runs off the UI thread; <see cref="SkyCD.Plugin.Runtime.Managers.PluginManager.ApplyDiscovery"/>
/// swaps the plugin set on the caller's thread.
/// </summary>
public sealed record PluginDiscovery(
    DiscoveredPluginCollection Discovered,
    IReadOnlyCollection<PluginLoadContext> LoadedContexts);
