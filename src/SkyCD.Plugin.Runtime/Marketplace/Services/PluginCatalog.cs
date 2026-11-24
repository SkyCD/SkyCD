using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Runtime.Marketplace.Models;
using SkyCD.Plugin.Runtime.Marketplace.Transport;

namespace SkyCD.Plugin.Runtime.Marketplace.Services;

/// <summary>
/// Read-only access to the plugin catalog for downstream issues (#803 install, #804 browse). The service is
/// independent of any UI framework: it resolves the awesome-skycd catalog over HTTPS, reads each publisher
/// manifest and selects the highest eligible package version per source.
/// </summary>
/// <remarks>
/// <para>
/// Explicit refresh: <see cref="RefreshAsync"/> always goes to the network. Concurrent refreshes are
/// serialized, so a burst of callers never multiplies the requests a source sees.
/// </para>
/// <para>
/// Cached read: <see cref="GetCatalogAsync"/> returns the last published result, so every caller sees the
/// same immutable snapshot and can rebind UI without reasoning about concurrent mutation. When the snapshot
/// expires (<see cref="PluginCatalogOptions.CacheLifetime"/>) the next read refreshes instead.
/// </para>
/// <para>
/// Last known good: when the catalog document cannot be read, the last source list is reused; when one
/// manifest cannot be read, its last accepted result is reused and the source is reported as offline. A
/// transient failure of one source never removes the healthy results of the others.
/// </para>
/// </remarks>
public sealed class PluginCatalog : IDisposable
{
    private readonly PluginCatalogResolver resolver;
    private readonly PluginRepositorySourceStore repositorySources;
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly TimeProvider clock;
    private readonly object stateGate = new();
    private PluginCatalogResult? current;
    private bool isDisposed;

    /// <summary>Creates a catalog that uses the default HTTPS fetcher, an in-memory cache and the system clock.</summary>
    public PluginCatalog(PluginCatalogOptions options)
        : this(options, new InMemoryPluginCatalogCache(), TimeProvider.System)
    {
    }

    public PluginCatalog(PluginCatalogOptions options, IPluginCatalogCache cache, TimeProvider? clock = null)
        : this(options, cache,
            HttpsPluginSourceFetcher.CreateDefault(options ?? throw new ArgumentNullException(nameof(options))),
            clock)
    {
    }

    /// <summary>Creates a catalog that also reads the user's repository manifests from <paramref name="repositorySources"/>.</summary>
    public PluginCatalog(PluginCatalogOptions options, PluginRepositorySourceStore repositorySources)
        : this(options, new InMemoryPluginCatalogCache(),
            HttpsPluginSourceFetcher.CreateDefault(options ?? throw new ArgumentNullException(nameof(options))),
            null, repositorySources)
    {
    }

    /// <summary>Creates a catalog with explicit boundaries; <paramref name="fetcher"/> is disposed with the service.</summary>
    public PluginCatalog(PluginCatalogOptions options, IPluginCatalogCache cache, IPluginSourceFetcher fetcher,
        TimeProvider? clock = null, PluginRepositorySourceStore? repositorySources = null)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(fetcher);
        this.clock = clock ?? TimeProvider.System;
        this.repositorySources = repositorySources ?? new PluginRepositorySourceStore();
        resolver = new PluginCatalogResolver(Options, cache, fetcher, this.clock, this.repositorySources);
    }

    public PluginCatalogOptions Options { get; }

    /// <summary>Repository URLs the user added beside the catalog; the next resolution reads them.</summary>
    public IReadOnlyList<Uri> RepositorySources => repositorySources.Sources;

    /// <summary>Replaces the user's repository URLs without invalidating the published snapshot.</summary>
    public void SetRepositorySources(IEnumerable<Uri>? sources)
    {
        repositorySources.SetSources(sources);
    }

    /// <summary>
    /// Replaces the user's repository URLs and forgets the published snapshot and last known good results, so
    /// the next read resolves against the new list instead of serving the previous one from cache.
    /// </summary>
    public async Task UpdateRepositorySourcesAsync(IEnumerable<Uri>? sources,
        CancellationToken cancellationToken = default)
    {
        if (!repositorySources.SetSources(sources))
        {
            return;
        }

        await ResetLastKnownGoodAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The most recent published snapshot, or <see langword="null"/> before the first resolution.</summary>
    public PluginCatalogSnapshot? CurrentSnapshot
    {
        get
        {
            lock (stateGate)
            {
                return current?.Snapshot;
            }
        }
    }

    /// <summary>Returns the published snapshot, refreshing first when nothing has been published yet or the
    /// snapshot expired. With no network the result still describes the offline state.</summary>
    public async Task<PluginCatalogResult> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        PluginCatalogResult? cached;
        lock (stateGate)
        {
            cached = current;
        }

        var now = clock.GetUtcNow();
        if (cached is not null && now < cached.Snapshot.ExpiresAt)
        {
            return cached;
        }

        return await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the catalog and records it as the last known good state. Call it once per session (or after
    /// <see cref="ResetLastKnownGoodAsync"/>); later failures fall back to that state instead of lying.
    /// </summary>
    public async Task<PluginCatalogResult> BootstrapAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        await refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            var result = await resolver.BootstrapAsync(cancellationToken).ConfigureAwait(false);
            lock (stateGate)
            {
                current = result;
            }

            return result;
        }
        finally
        {
            refreshGate.Release();
        }
    }

    /// <summary>
    /// Fetches the catalog now, regardless of the cache lifetime. Failing sources fall back to their last
    /// known good result. Concurrent calls are serialized; a caller cancelled while waiting does not cancel
    /// the running refresh.
    /// </summary>
    public async Task<PluginCatalogResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        await refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            var result = await resolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
            lock (stateGate)
            {
                current = result;
            }

            return result;
        }
        finally
        {
            refreshGate.Release();
        }
    }

    /// <summary>
    /// Makes the next <see cref="GetCatalogAsync"/> refresh. Only an explicit reset forgets the last known
    /// good source list and manifests; an ordinary refresh never does.
    /// </summary>
    public async Task ResetLastKnownGoodAsync(CancellationToken cancellationToken = default)
    {
        await refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (stateGate)
            {
                current = null;
            }

            await resolver.ClearLastKnownGoodAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            refreshGate.Release();
        }
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        refreshGate.Dispose();
        (resolver.Fetcher as IDisposable)?.Dispose();
    }
}
