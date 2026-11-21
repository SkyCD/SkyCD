using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Runtime.Installation;
using SkyCD.Plugin.Runtime.Marketplace.Models;

namespace SkyCD.Presentation.Marketplace;

/// <summary>
/// Boundary between the plugin Browse view and the marketplace services so the view model can be tested
/// without network access or a real plugin folder.
/// </summary>
public interface IPluginMarketplaceService
{
    /// <summary>Resolves the catalog, reusing the last known good state for sources that fail.</summary>
    Task<PluginCatalogSnapshot> LoadAsync(bool forceRefresh, CancellationToken cancellationToken);

    /// <summary>Stages the release for activation on the next restart.</summary>
    Task<PluginInstallResult> StageInstallAsync(MarketplaceRelease release, CancellationToken cancellationToken);

    /// <summary>Catalog-owned install states keyed by plugin id.</summary>
    IReadOnlyDictionary<string, PluginInstallState> GetInstallStates();
}
