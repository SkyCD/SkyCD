namespace SkyCD.Plugin.Runtime.Installation.Exceptions;

/// <summary>The package download failed, timed out, was cancelled, or exceeded the configured size limit.</summary>
public sealed class PluginPackageDownloadException(string packageId, string version, string detail)
    : PluginInstallException($"Download of package '{packageId}' version '{version}' failed: {detail}.",
        PluginInstallFailureReason.DownloadFailed);
