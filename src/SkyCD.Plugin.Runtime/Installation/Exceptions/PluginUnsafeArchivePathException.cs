namespace SkyCD.Plugin.Runtime.Installation.Exceptions;

/// <summary>The package contains an entry that is absolute, escapes the package root, is duplicated, or is a link.</summary>
public sealed class PluginUnsafeArchivePathException(string packageId, string entryName)
    : PluginInstallException($"Package '{packageId}' contains an unsafe archive entry '{entryName}'.",
        PluginInstallFailureReason.UnsafeArchivePath)
{
    public string EntryName { get; } = entryName;
}
