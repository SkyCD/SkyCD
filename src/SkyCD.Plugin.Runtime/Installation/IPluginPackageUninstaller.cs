using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Runtime.Installation;

/// <summary>
/// Removes a catalog-owned plugin installation from disk. Implementations resolve the install folder from the
/// plugin id through <see cref="PluginInstallLayout"/> and refuse every folder the catalog does not own.
/// </summary>
public interface IPluginPackageUninstaller
{
    /// <summary>Deletes the catalog install folder, its pending operation and its backups, or refuses with a reason.</summary>
    Task<PluginInstallResult> UninstallAsync(string pluginId, CancellationToken cancellationToken = default);
}
