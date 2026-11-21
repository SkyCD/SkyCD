using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Runtime.Installation;
using SkyCD.Plugin.Runtime.Marketplace.Models;
using SkyCD.Plugin.Runtime.Marketplace.Services;
using SkyCD.Presentation.Marketplace;

namespace SkyCD.App.Marketplace;

/// <summary>
/// Desktop-side marketplace facade: the catalog supplies browsing data, the installer stages downloads and the
/// install-state source reports what is active or waiting for a restart.
/// </summary>
public sealed class PluginMarketplaceService(
    PluginCatalog catalog,
    IPluginPackageInstaller installer,
    IPluginInstallStateSource installStates) : IPluginMarketplaceService
{
    public async Task<PluginCatalogSnapshot> LoadAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var result = forceRefresh
            ? await catalog.RefreshAsync(cancellationToken).ConfigureAwait(false)
            : await catalog.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
        return result.Snapshot;
    }

    public Task<PluginInstallResult> StageInstallAsync(MarketplaceRelease release, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        var request = new PluginPackageRequest(
            release.FeedUri.AbsoluteUri,
            release.PackageId,
            release.Version.Normalized);
        return installer.StageInstallAsync(request, cancellationToken);
    }

    public IReadOnlyDictionary<string, PluginInstallState> GetInstallStates() => installStates.GetInstallStates();
}
