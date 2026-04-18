namespace SkyCD.Plugin.Runtime.Loading;

/// <summary>
/// Configurable options for manifest-based plugin loading.
/// </summary>
public sealed class PluginLoadOptions
{
    public required Version HostVersion { get; init; }

    public bool EnableAssemblyIsolation { get; init; }
}
