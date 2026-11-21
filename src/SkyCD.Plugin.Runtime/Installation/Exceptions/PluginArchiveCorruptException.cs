namespace SkyCD.Plugin.Runtime.Installation.Exceptions;

/// <summary>The downloaded package is not a readable NuGet package: invalid zip data, no manifest, or an oversized entry.</summary>
public sealed class PluginArchiveCorruptException(string packageId, string detail)
    : PluginInstallException($"Package '{packageId}' is not a valid plugin package: {detail}.",
        PluginInstallFailureReason.ArchiveCorrupt);
