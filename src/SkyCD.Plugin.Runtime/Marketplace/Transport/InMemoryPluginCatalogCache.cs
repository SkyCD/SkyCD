using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Runtime.Marketplace.Transport;

/// <summary>Process-lifetime last-known-good store; the service itself never holds mutable cache state.</summary>
public sealed class InMemoryPluginCatalogCache : IPluginCatalogCache
{
    private readonly ConcurrentDictionary<string, PublisherManifestCacheEntry> manifests = new(StringComparer.Ordinal);
    private volatile PluginCatalogCacheEntry? catalog;

    public Task<PluginCatalogCacheEntry?> GetCatalogAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(catalog);
    }

    public Task SetCatalogAsync(PluginCatalogCacheEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        catalog = entry;
        return Task.CompletedTask;
    }

    public Task<PublisherManifestCacheEntry?> GetManifestAsync(Uri manifestUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifestUri);
        manifests.TryGetValue(manifestUri.AbsoluteUri, out var entry);
        return Task.FromResult(entry);
    }

    public Task SetManifestAsync(PublisherManifestCacheEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        manifests[entry.ManifestUri.AbsoluteUri] = entry;
        return Task.CompletedTask;
    }

    public Task RemoveManifestAsync(Uri manifestUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifestUri);
        manifests.TryRemove(manifestUri.AbsoluteUri, out _);
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        catalog = null;
        manifests.Clear();
        return Task.CompletedTask;
    }
}
