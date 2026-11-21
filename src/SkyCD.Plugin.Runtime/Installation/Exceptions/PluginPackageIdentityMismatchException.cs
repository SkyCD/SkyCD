namespace SkyCD.Plugin.Runtime.Installation.Exceptions;

/// <summary>The package manifest declares a different id or version than the requested package.</summary>
public sealed class PluginPackageIdentityMismatchException(string expectedId, string expectedVersion,
    string actualId, string actualVersion)
    : PluginInstallException(
        $"Package identity '{actualId}' version '{actualVersion}' does not match the requested '{expectedId}' version '{expectedVersion}'.",
        PluginInstallFailureReason.PackageIdentityMismatch);
