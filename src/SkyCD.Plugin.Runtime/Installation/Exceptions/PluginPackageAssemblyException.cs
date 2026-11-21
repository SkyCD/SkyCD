namespace SkyCD.Plugin.Runtime.Installation.Exceptions;

/// <summary>The package does not contain a readable plugin assembly with a valid plugin identity.</summary>
public sealed class PluginPackageAssemblyException(string packageId, string detail)
    : PluginInstallException($"Package '{packageId}' does not contain a valid plugin assembly: {detail}.",
        PluginInstallFailureReason.InvalidPluginAssembly);
