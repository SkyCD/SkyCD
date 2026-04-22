using SkyCD.Plugin.Runtime.Discovery;

namespace SkyCD.Plugin.Runtime.Loading;

/// <summary>
///     Result of manifest-based plugin loading.
/// </summary>
public sealed class PluginLoadResult
{
    public IReadOnlyCollection<DiscoveredPlugin> Plugins { get; init; } = [];

    public IReadOnlyCollection<PluginLoadDiagnostic> Diagnostics { get; init; } = [];
}