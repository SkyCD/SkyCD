namespace SkyCD.Plugin.Runtime.Installation.Exceptions;

/// <summary>
/// The package would replace a different plugin identity, a manually installed/bundled plugin folder,
/// or a catalog plugin claimed by another pending operation.
/// </summary>
public sealed class PluginIdentityConflictException(string pluginId, string conflictingPluginId, string detail)
    : PluginInstallException(
        $"Package for plugin '{pluginId}' conflicts with plugin '{conflictingPluginId}': {detail}.",
        PluginInstallFailureReason.IdentityConflict);
