using System;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Runtime.Marketplace.Transport;

/// <summary>
/// Small persistence boundary for last-known-good catalog state. The default implementation is in memory;
/// a disk-backed implementation can be supplied without changing the service.
/// </summary>
public interface IPluginCatalogCache
{
    Task<PluginCatalogCacheEntry?> GetCatalogAsync(CancellationToken cancellationToken);

    Task SetCatalogAsync(PluginCatalogCacheEntry entry, CancellationToken cancellationToken);

    Task<PublisherManifestCacheEntry?> GetManifestAsync(Uri manifestUri, CancellationToken cancellationToken);

    Task SetManifestAsync(PublisherManifestCacheEntry entry, CancellationToken cancellationToken);

    Task RemoveManifestAsync(Uri manifestUri, CancellationToken cancellationToken);

    /// <summary>Forgets every stored catalog and manifest; used only by an explicit reset.</summary>
    Task ClearAsync(CancellationToken cancellationToken);
}
