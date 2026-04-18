using SkyCD.Plugin.Abstractions.Lifecycle;

namespace SkyCD.Plugin.Runtime.Discovery;

/// <summary>
/// Evaluates whether a plugin is compatible with the current host version.
/// </summary>
public static class PluginCompatibilityEvaluator
{
    public static bool IsCompatible(PluginDescriptor descriptor, Version hostVersion)
    {
        return hostVersion >= descriptor.MinHostVersion;
    }
}
