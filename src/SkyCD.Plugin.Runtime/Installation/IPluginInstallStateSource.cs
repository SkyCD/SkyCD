using System.Collections.Generic;

namespace SkyCD.Plugin.Runtime.Installation;

/// <summary>
/// Read-only catalog install state for every discovered plugin, including manual and bundled plugins,
/// which are reported with <see cref="PluginInstallOperationStatus.NotInstalled"/> because the catalog does not own them.
/// </summary>
public interface IPluginInstallStateSource
{
    /// <summary>State of the catalog-owned operations that exist under the configured plugin root.</summary>
    IReadOnlyDictionary<string, PluginInstallState> GetInstallStates();

    /// <summary>Current restart-conditioned operation for the plugin, including failure details.</summary>
    PluginInstallState GetInstallState(string pluginId);
}
