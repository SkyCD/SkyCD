using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Runtime.Installation;

/// <summary>
/// Stages catalog plugin installs and updates. Staged operations activate only when the host restarts,
/// immediately before plugin discovery enumerates the plugin root.
/// </summary>
public interface IPluginPackageInstaller
{
    /// <summary>
    /// Downloads and validates the exact package into staging, writes the pending operation and returns
    /// <see cref="PluginInstallOperationStatus.PendingRestart"/>. Concurrent calls for the same plugin are serialized;
    /// repeating a staged operation returns the existing state without downloading again.
    /// </summary>
    Task<PluginInstallResult> StageInstallAsync(PluginPackageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies every pending operation before plugin discovery. Safe to call on each startup; returns one result
    /// per applied, failed or rolled-back operation.
    /// </summary>
    Task<IReadOnlyList<PluginInstallResult>> ApplyPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>Discards a pending operation and its staged content without touching the active version.</summary>
    Task<PluginInstallResult> CancelPendingAsync(string pluginId, CancellationToken cancellationToken = default);
}
