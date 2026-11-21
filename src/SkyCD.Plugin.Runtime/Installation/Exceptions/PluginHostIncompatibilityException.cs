namespace SkyCD.Plugin.Runtime.Installation.Exceptions;

/// <summary>The package's plugin declares a host version range that excludes the running host.</summary>
public sealed class PluginHostIncompatibilityException(string pluginId, string pluginVersion, string minHostVersion,
    string? maxHostVersion, string hostVersion)
    : PluginInstallException(
        $"Plugin '{pluginId}' version '{pluginVersion}' requires host version '{minHostVersion}' to '{maxHostVersion ?? "any"}' but the host is '{hostVersion}'.",
        PluginInstallFailureReason.IncompatibleHostVersion);
