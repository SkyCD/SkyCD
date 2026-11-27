using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Runtime.Marketplace.Exceptions;
using SkyCD.Plugin.Runtime.Marketplace.Models;
using SkyCD.Plugin.Runtime.Marketplace.Transport;

namespace SkyCD.Plugin.Runtime.Marketplace.Services;

/// <summary>One network resolution of the catalog. Holds no state that has to be shared between refreshes.</summary>
internal sealed class PluginCatalogResolver
{
    private readonly PluginCatalogOptions options;
    private readonly IPluginCatalogCache cache;
    private readonly PluginRepositorySourceStore repositorySources;
    private readonly TimeProvider clock;

    public PluginCatalogResolver(PluginCatalogOptions options, IPluginCatalogCache cache, IPluginSourceFetcher fetcher,
        TimeProvider clock, PluginRepositorySourceStore? repositorySources = null)
    {
        this.options = options;
        this.cache = cache;
        this.clock = clock;
        this.repositorySources = repositorySources ?? new PluginRepositorySourceStore();
        Fetcher = fetcher;
    }

    public IPluginSourceFetcher Fetcher { get; }

    /// <summary>Forgets the last known good catalog and manifests.</summary>
    public Task ClearLastKnownGoodAsync(CancellationToken cancellationToken)
    {
        return cache.ClearAsync(cancellationToken);
    }

    /// <summary>
    /// First resolution, performed once per service instance and after an explicit reset. This is the only
    /// pass that writes the last known good state; a failed source simply has no result.
    /// </summary>
    public async Task<PluginCatalogResult> BootstrapAsync(CancellationToken cancellationToken)
    {
        var diagnostics = new List<string>();
        var catalog = AppendRepositorySources(await ResolveCatalogAsync(fallbackAllowed: false, diagnostics,
            cancellationToken).ConfigureAwait(false));
        var manifests = await ResolveManifestsAsync(catalog.Sources, fallbackAllowed: false, diagnostics,
            cancellationToken).ConfigureAwait(false);
        return Complete(catalog, manifests, diagnostics);
    }

    /// <summary>
    /// One refresh: catalog first, then manifests in parallel. A failing source falls back to its last known
    /// good result, so healthy results are preserved; this pass never writes the last known good state.
    /// </summary>
    public async Task<PluginCatalogResult> ResolveAsync(CancellationToken cancellationToken)
    {
        var diagnostics = new List<string>();
        var catalog = AppendRepositorySources(await ResolveCatalogAsync(fallbackAllowed: true, diagnostics,
            cancellationToken).ConfigureAwait(false));
        var manifests = await ResolveManifestsAsync(catalog.Sources, fallbackAllowed: true, diagnostics,
            cancellationToken).ConfigureAwait(false);
        return Complete(catalog, manifests, diagnostics);
    }

    private PluginCatalogResult Complete(CatalogResolution catalog, IReadOnlyList<PluginSourceSnapshot> manifests,
        List<string> diagnostics)
    {
        var packages = manifests
            .SelectMany(static source => source.Packages)
            .OrderBy(static package => package, Comparer<MarketplacePackage>.Create(MarketplacePackage.CompareBestFirst))
            .ThenBy(static package => package.SourceUri.AbsoluteUri, StringComparer.Ordinal)
            .ToArray();

        var now = clock.GetUtcNow();
        var snapshot = new PluginCatalogSnapshot
        {
            ResolvedAt = now,
            ExpiresAt = now + options.CacheLifetime,
            CatalogEndpoint = catalog.Endpoint,
            CatalogStatus = catalog.Status,
            CatalogDetail = PluginSourceStatusDetail.ForCatalog(catalog.Status),
            Generation = catalog.Generation,
            Sources = manifests,
            Packages = packages,
            BestReleases = PluginCatalogRules.SelectBestReleases(packages)
        };

        return new PluginCatalogResult
        {
            Snapshot = snapshot,
            Diagnostics = diagnostics.Distinct(StringComparer.Ordinal).ToArray(),
            SourceUrls = catalog.Sources.Select(static source => source.ManifestUri.AbsoluteUri).ToArray()
        };
    }

    /// <summary>
    /// Merges the user's repository manifests into the catalog sources. A URL the catalog already lists keeps
    /// its catalog entry, so one repository never contributes its packages twice.
    /// </summary>
    private CatalogResolution AppendRepositorySources(CatalogResolution catalog)
    {
        var configured = repositorySources.Sources;
        if (configured.Count == 0)
        {
            return catalog;
        }

        var known = new HashSet<string>(catalog.Sources.Select(static source => source.ManifestUri.AbsoluteUri),
            StringComparer.Ordinal);
        var sources = catalog.Sources.ToList();
        foreach (var uri in configured)
        {
            if (!known.Add(uri.AbsoluteUri))
            {
                continue;
            }

            sources.Add(new PluginSourceSnapshot
            {
                ManifestUri = uri,
                Status = PluginSourceStatus.Loaded,
                Detail = PluginSourceStatusDetail.Create(PluginSourceStatus.Loaded, 0)
            });
        }

        return sources.Count == catalog.Sources.Count ? catalog : catalog with { Sources = sources };
    }

    private async Task<CatalogResolution> ResolveCatalogAsync(bool fallbackAllowed, List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var cached = fallbackAllowed
            ? await cache.GetCatalogAsync(cancellationToken).ConfigureAwait(false)
            : null;

        if (options.CatalogEndpoints.Count == 0)
        {
            return new CatalogResolution([], PluginCatalogStatus.NotConfigured, null, 0);
        }

        foreach (var endpoint in options.CatalogEndpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!PluginCatalogSourceDocuments.IsHttps(endpoint))
            {
                diagnostics.Add($"Catalog endpoint '{endpoint}' was rejected: it is not an absolute HTTPS URL.");
                continue;
            }

            string text;
            try
            {
                text = await Fetcher.GetStringAsync(endpoint, options.MaxCatalogBytes, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (PluginSourceProtocolException exception)
            {
                diagnostics.Add($"Catalog '{endpoint}' was rejected: {exception.Message}");
                continue;
            }
            catch (Exception exception) when (exception is PluginSourceUnavailableException or
                                                  PluginSourceResponseTooLargeException)
            {
                diagnostics.Add($"Catalog '{endpoint}' could not be read: {exception.Message}");
                continue;
            }

            IReadOnlyList<string> entries;
            try
            {
                entries = PluginCatalogSourceDocuments.ParseEntries(endpoint, text);
            }
            catch (MalformedPluginCatalogException exception)
            {
                diagnostics.Add(exception.Message);
                continue;
            }

            var (sources, valid) = BuildSources(entries, diagnostics);

            if (!fallbackAllowed)
            {
                await cache.SetCatalogAsync(new PluginCatalogCacheEntry(endpoint, valid, clock.GetUtcNow()),
                    cancellationToken).ConfigureAwait(false);
            }

            return new CatalogResolution(sources, PluginCatalogStatus.Loaded, endpoint, 1);
        }

        if (cached is null)
        {
            return new CatalogResolution([], PluginCatalogStatus.Offline, options.CatalogEndpoints[0], 0);
        }

        var offline = new List<PluginSourceSnapshot>();
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in cached.SourceUrls)
        {
            if (!PluginCatalogSourceDocuments.TryNormalize(entry, out var uri))
            {
                continue;
            }

            if (known.Add(uri.AbsoluteUri))
            {
                offline.Add(new PluginSourceSnapshot
                {
                    ManifestUri = uri,
                    Status = PluginSourceStatus.Offline,
                    Detail = PluginSourceStatusDetail.Create(PluginSourceStatus.Offline, 0)
                });
            }
        }

        return new CatalogResolution(offline, PluginCatalogStatus.Offline, cached.Endpoint, 1);
    }

    /// <summary>
    /// Turns catalog entries into source snapshots. Invalid entries become rejected placeholders and exact
    /// duplicate URLs are skipped, so one bad entry never rejects the catalog; the surviving URLs are the
    /// ones worth remembering as last known good.
    /// </summary>
    private static (List<PluginSourceSnapshot> Sources, List<string> Valid) BuildSources(IReadOnlyList<string> entries,
        List<string> diagnostics)
    {
        var sources = new List<PluginSourceSnapshot>();
        var valid = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!PluginCatalogSourceDocuments.TryNormalize(entry, out var uri))
            {
                diagnostics.Add($"Catalog entry '{entry}' was rejected: it is not an absolute HTTPS URL.");
                sources.Add(new PluginSourceSnapshot
                {
                    ManifestUri = PluginCatalogSourceDocuments.PlaceholderFor(entry),
                    RawEntry = entry,
                    Status = PluginSourceStatus.Rejected,
                    Detail = PluginSourceStatusDetail.Create(PluginSourceStatus.Rejected, 0)
                });
                continue;
            }

            if (!seen.Add(uri.AbsoluteUri))
            {
                diagnostics.Add($"Catalog entry '{entry}' appears more than once; the duplicate was skipped.");
                continue;
            }

            valid.Add(uri.AbsoluteUri);
            sources.Add(new PluginSourceSnapshot
            {
                ManifestUri = uri,
                Status = PluginSourceStatus.Loaded,
                Detail = PluginSourceStatusDetail.Create(PluginSourceStatus.Loaded, 0)
            });
        }

        return (sources, valid);
    }

    private async Task<IReadOnlyList<PluginSourceSnapshot>> ResolveManifestsAsync(
        IReadOnlyList<PluginSourceSnapshot> catalog, bool fallbackAllowed, List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(Math.Max(1, options.MaxSourceConcurrency));
        var tasks = catalog
            .Select(source => ResolveManifestGuardedAsync(source, fallbackAllowed, gate, diagnostics, cancellationToken))
            .ToArray();
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task<PluginSourceSnapshot> ResolveManifestGuardedAsync(PluginSourceSnapshot source,
        bool fallbackAllowed, SemaphoreSlim gate, List<string> diagnostics, CancellationToken cancellationToken)
    {
        if (source.Status == PluginSourceStatus.Rejected)
        {
            return source;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ResolveManifestAsync(source, fallbackAllowed, diagnostics, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<PluginSourceSnapshot> ResolveManifestAsync(PluginSourceSnapshot source, bool fallbackAllowed,
        List<string> diagnostics, CancellationToken cancellationToken)
    {
        string text;
        try
        {
            text = await Fetcher.GetStringAsync(source.ManifestUri, options.MaxManifestBytes, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PluginSourceProtocolException exception)
        {
            diagnostics.Add($"Source '{source.ManifestUri}' was rejected: {exception.Message}");
            return source with
            {
                Status = PluginSourceStatus.Rejected,
                Detail = PluginSourceStatusDetail.Create(PluginSourceStatus.Rejected, 0)
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            diagnostics.Add($"Source '{source.ManifestUri}' is unavailable: {exception.Message}");
            return fallbackAllowed
                ? await ResolveFromCacheAsync(source, PluginSourceStatus.Offline, cancellationToken)
                    .ConfigureAwait(false)
                : source with
                {
                    Status = PluginSourceStatus.Offline,
                    Detail = PluginSourceStatusDetail.Create(PluginSourceStatus.Offline, 0)
                };
        }

        var read = PublisherManifestDocuments.Read(text, options.MaxPackagesPerManifest,
            options.MaxReleasesPerPackage);
        switch (read.Status)
        {
            case ManifestReadStatus.UnsupportedSchema:
                diagnostics.Add(
                    $"Source '{source.ManifestUri}' uses unsupported manifest schema version {read.SchemaVersion}.");
                await cache.RemoveManifestAsync(source.ManifestUri, cancellationToken).ConfigureAwait(false);
                return source with
                {
                    Status = PluginSourceStatus.UnsupportedSchema,
                    Detail = PluginSourceStatusDetail.Create(PluginSourceStatus.UnsupportedSchema, read.SchemaVersion)
                };

            case ManifestReadStatus.Malformed:
                diagnostics.Add($"Source '{source.ManifestUri}' is malformed: {read.Reason}");
                await cache.RemoveManifestAsync(source.ManifestUri, cancellationToken).ConfigureAwait(false);
                return source with
                {
                    Status = PluginSourceStatus.Malformed,
                    Detail = PluginSourceStatusDetail.Create(PluginSourceStatus.Malformed, read.SchemaVersion)
                };
        }

        var manifest = read.Manifest!;
        Uri? feed = null;
        if (!PublisherManifestDocuments.TryParseHttpsUri(manifest.FeedText, out feed))
        {
            diagnostics.Add($"Source '{source.ManifestUri}' declares a feed that is not an absolute HTTPS URL.");
        }

        Uri? publisherUrl = null;
        PublisherManifestDocuments.TryParseHttpsUri(manifest.ProjectUrl, out publisherUrl);

        var packages = new List<MarketplacePackage>();
        var releases = new List<CachedManifestRelease>();
        foreach (var package in manifest.Packages)
        {
            packages.Add(await ResolvePackageAsync(source.ManifestUri, feed, package, releases, diagnostics,
                    cancellationToken)
                .ConfigureAwait(false));
        }

        if (!fallbackAllowed)
        {
            var entry = new PublisherManifestCacheEntry(
                source.ManifestUri,
                PluginCatalogSourceDocuments.Fingerprint(text),
                releases,
                manifest.Publisher,
                publisherUrl,
                manifest.Packages.Select(static package => package.Id).ToArray());
            await cache.SetManifestAsync(entry, cancellationToken).ConfigureAwait(false);
        }

        return source with
        {
            Status = PluginSourceStatus.Loaded,
            Detail = PluginSourceStatusDetail.Create(PluginSourceStatus.Loaded, 0),
            Publisher = manifest.Publisher,
            ProjectUrl = publisherUrl,
            Packages = packages
        };
    }

    private async Task<MarketplacePackage> ResolvePackageAsync(Uri sourceUri, Uri? feed,
        ParsedManifestPackage package, List<CachedManifestRelease> collected, List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var versions = feed is null
            ? FeedVersions.Unlisted()
            : await ResolveVersionsAsync(feed, package.Id, diagnostics, cancellationToken).ConfigureAwait(false);

        Uri? projectUrl = null;
        PublisherManifestDocuments.TryParseHttpsUri(package.ProjectUrl, out projectUrl);

        var releases = new List<MarketplaceRelease>();
        foreach (var release in package.Releases)
        {
            var definition = new PluginReleaseDefinition(release.Version, release.MinHostVersion,
                release.MaxHostVersion, release.Platforms);
            var report = PluginCatalogRules.Evaluate(definition, options);
            var excluded = PluginCatalogRules.ApplyAvailability(report, definition, versions.Availability, options);
            report = excluded ?? report;

            collected.Add(new CachedManifestRelease(
                package.Id,
                release.Version.Original,
                release.MinHostVersion,
                release.MaxHostVersion,
                release.Platforms,
                package.DisplayName,
                package.Summary,
                package.License,
                package.ProjectUrl,
                feed?.AbsoluteUri ?? string.Empty,
                new CachedManifestReleaseCompatibility(report.Status, report.Reason, report.Message),
                package.Icon));

            releases.Add(new MarketplaceRelease
            {
                SourceUri = sourceUri,
                FeedUri = feed ?? Placeholder,
                PackageContentBaseUri = versions.BaseAddress,
                RegisteredPackageContentUri = versions.Registration?.GetValueOrDefault(release.Version.Normalized),
                PackageId = package.Id,
                Version = release.Version,
                MinHostVersion = release.MinHostVersion,
                MaxHostVersion = release.MaxHostVersion,
                Platforms = release.Platforms,
                Compatibility = report
            });
        }

        var ordered = releases
            .OrderBy(static release => release, Comparer<MarketplaceRelease>.Create(MarketplaceRelease.CompareBestFirst))
            .ToArray();

        return new MarketplacePackage
        {
            SourceUri = sourceUri,
            PackageId = package.Id,
            DisplayName = package.DisplayName,
            Summary = package.Summary,
            License = package.License,
            ProjectUrl = projectUrl,
            Icon = package.Icon,
            FeedUri = feed,
            Releases = ordered,
            BestRelease = ordered.FirstOrDefault(static release => release.IsEligible)
        };
    }

    private async Task<FeedVersions> ResolveVersionsAsync(Uri feed, string packageId, List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        try
        {
            var serviceIndex = await Fetcher.GetStringAsync(feed, options.MaxFeedDocumentBytes, cancellationToken)
                .ConfigureAwait(false);
            var baseAddress = NuGetFeedDocuments.ParsePackageBaseAddress(feed, serviceIndex);
            if (baseAddress is null)
            {
                var registration = NuGetFeedDocuments.ParseRegistrationBaseAddress(feed, serviceIndex);
                if (registration is null)
                {
                    return FeedVersions.Unlisted();
                }

                var content = await NuGetFeedDocuments.ReadRegistrationAsync(registration, packageId,
                    (uri, token) => Fetcher.GetStringAsync(uri, options.MaxFeedDocumentBytes, token),
                    cancellationToken).ConfigureAwait(false);
                return new FeedVersions(feed.AbsoluteUri, null, new HashSet<string>(content.Keys, content.Comparer),
                    null, content);
            }

            var rootUri = baseAddress.AbsoluteUri.EndsWith('/')
                ? baseAddress
                : new Uri(baseAddress.AbsoluteUri + "/");
            var id = packageId.ToLowerInvariant();
            var versionList = new Uri(rootUri, $"{id}/index.json");
            var json = await Fetcher.GetStringAsync(versionList, options.MaxFeedDocumentBytes, cancellationToken)
                .ConfigureAwait(false);
            var listed = NuGetFeedDocuments.ParseVersions(versionList, json);

            var versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var text in listed)
            {
                if (PluginPackageVersion.TryParse(text, out var parsed))
                {
                    versions.Add(parsed!.Normalized);
                }
            }

            return new FeedVersions(feed.AbsoluteUri, rootUri, versions, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The manifest still describes the package; availability is simply unknown this time.
            diagnostics.Add($"Feed '{feed}' could not be read: {exception.Message}");
            return FeedVersions.Failed(feed);
        }
    }

    private async Task<PluginSourceSnapshot> ResolveFromCacheAsync(PluginSourceSnapshot source,
        PluginSourceStatus status, CancellationToken cancellationToken)
    {
        var detail = PluginSourceStatusDetail.Create(status, 0);
        var entry = await cache.GetManifestAsync(source.ManifestUri, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return source with { Status = status, Detail = detail };
        }

        var packages = entry.PackageIds
            .Select(id => entry.Releases
                .Where(release => string.Equals(release.PackageId, id, StringComparison.Ordinal))
                .ToArray())
            .Where(static releases => releases.Length > 0)
            .Select(releases => BuildPackageFromCache(source.ManifestUri, releases))
            .ToArray();

        return source with
        {
            Status = status,
            Detail = detail,
            IsStale = true,
            Publisher = entry.Publisher,
            ProjectUrl = entry.ProjectUrl,
            Packages = packages
        };
    }

    /// <summary>
    /// Rebuilds a package from the cache. Cached verdicts never include feed availability, so an eligible
    /// release is re-marked unknown: it has not been confirmed against the feed this time and therefore must
    /// not be selected automatically.
    /// </summary>
    private static MarketplacePackage BuildPackageFromCache(Uri manifestUri, IReadOnlyList<CachedManifestRelease> releases)
    {
        var first = releases[0];
        Uri? feed = null;
        PublisherManifestDocuments.TryParseHttpsUri(first.FeedText, out feed);

        var marketplaceReleases = new List<MarketplaceRelease>();
        foreach (var release in releases)
        {
            if (!PluginPackageVersion.TryParse(release.Version, out var version))
            {
                continue;
            }

            var report = PluginCompatibilityReport.FromCache(release.Compatibility.Status, release.Compatibility.Reason,
                release.Compatibility.Message);
            if (report.IsCompatible)
            {
                report = PluginCompatibilityReport.Unknown("the source is offline; availability was not confirmed.");
            }

            marketplaceReleases.Add(new MarketplaceRelease
            {
                SourceUri = manifestUri,
                FeedUri = feed ?? Placeholder,
                PackageId = release.PackageId,
                Version = version!,
                MinHostVersion = release.MinHostVersion,
                MaxHostVersion = release.MaxHostVersion,
                Platforms = release.Platforms,
                Compatibility = report
            });
        }

        var ordered = marketplaceReleases
            .OrderBy(static release => release, Comparer<MarketplaceRelease>.Create(MarketplaceRelease.CompareBestFirst))
            .ToArray();

        return new MarketplacePackage
        {
            SourceUri = manifestUri,
            PackageId = first.PackageId,
            DisplayName = first.DisplayName,
            Summary = first.Summary,
            License = first.License,
            Icon = first.Icon,
            FeedUri = feed,
            Releases = ordered,
            BestRelease = null
        };
    }

    private static readonly Uri Placeholder = new("https://invalid.invalid/");

    private sealed record CatalogResolution(
        IReadOnlyList<PluginSourceSnapshot> Sources,
        PluginCatalogStatus Status,
        Uri? Endpoint,
        int Generation);

    private sealed record FeedVersions(string? Feed, Uri? BaseAddress, HashSet<string> Versions,
        FeedAvailability? State, IReadOnlyDictionary<string, Uri>? Registration = null)
    {
        /// <summary>The feed cannot confirm anything: no package content resource (or no usable feed URL).</summary>
        public static FeedVersions Unlisted()
        {
            return new FeedVersions(null, null, [], FeedAvailability.Failed(
                "the feed exposes no package content or registration resource."));
        }

        public static FeedVersions Failed(Uri feed)
        {
            return new FeedVersions(feed.AbsoluteUri, null, [],
                FeedAvailability.Failed($"the feed '{feed}' could not be read."));
        }

        public FeedAvailability Availability(PluginPackageVersion version)
        {
            if (State is not null)
            {
                return State;
            }

            return Versions.Contains(version.Normalized)
                ? FeedAvailability.Available
                : FeedAvailability.Missing($"The feed '{Feed}' does not list this version.");
        }
    }
}
