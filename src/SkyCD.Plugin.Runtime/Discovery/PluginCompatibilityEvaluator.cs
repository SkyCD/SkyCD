using SkyCD.Plugin.Abstractions.Lifecycle;

namespace SkyCD.Plugin.Runtime.Discovery;

/// <summary>
/// Evaluates whether a plugin is compatible with the current host version.
/// </summary>
public static class PluginCompatibilityEvaluator
{
    public static bool IsCompatible(Version minHostVersion, Version? maxHostVersion, Version hostVersion)
    {
        return hostVersion >= minHostVersion &&
               (maxHostVersion is null || hostVersion <= maxHostVersion);
    }
}
