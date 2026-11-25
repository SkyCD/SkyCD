using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.Runtime.Marketplace.Transport;

/// <summary>Cached source list of the catalog document plus the endpoint that produced it.</summary>
public sealed record PluginCatalogCacheEntry(Uri Endpoint, IReadOnlyList<string> SourceUrls, DateTimeOffset FetchedAt);

/// <summary>Cached state of one publisher manifest.</summary>
/// <param name="ManifestUri">Manifest URL.</param>
/// <param name="DocumentVersion">Schema-independent hash of the manifest text, for change detection.</param>
/// <param name="Releases">Accepted releases with their feed-independent verdict.</param>
/// <param name="Publisher">Publisher name.</param>
/// <param name="ProjectUrl">Publisher URL.</param>
/// <param name="PackageIds">Package IDs in manifest order, so last known good results keep their order.</param>
public sealed record PublisherManifestCacheEntry(
    Uri ManifestUri,
    string DocumentVersion,
    IReadOnlyList<CachedManifestRelease> Releases,
    string? Publisher,
    Uri? ProjectUrl,
    IReadOnlyList<string> PackageIds);

/// <summary>A manifest release as cached, still bound to nothing but its manifest.</summary>
/// <param name="PackageId">Package ID as the manifest writes it.</param>
/// <param name="Version">Version text.</param>
/// <param name="MinHostVersion">Minimum host version.</param>
/// <param name="MaxHostVersion">Optional maximum host version.</param>
/// <param name="Platforms">Runtime identifiers.</param>
/// <param name="DisplayName">Package display name.</param>
/// <param name="Summary">Package summary.</param>
/// <param name="License">Package license.</param>
/// <param name="ProjectUrl">Package project URL text.</param>
/// <param name="FeedText">Feed URL text declared by the manifest.</param>
/// <param name="Compatibility">Verdict that does not depend on the feed. An eligible release is stored as
/// compatible; feed availability is never cached because it changes without a manifest edit.</param>
/// <param name="Icon">Package icon key declared by the manifest, when any.</param>
public sealed record CachedManifestRelease(
    string PackageId,
    string Version,
    Version MinHostVersion,
    Version? MaxHostVersion,
    IReadOnlyList<string> Platforms,
    string? DisplayName,
    string? Summary,
    string? License,
    string? ProjectUrl,
    string FeedText,
    CachedManifestReleaseCompatibility Compatibility,
    string? Icon = null);

/// <summary>Eligibility verdict stored with a cached release.</summary>
/// <param name="Status">Compatible or incompatible.</param>
/// <param name="Reason">Technical reason.</param>
/// <param name="Message">User-readable explanation.</param>
public sealed record CachedManifestReleaseCompatibility(
    Models.PluginCompatibilityStatus Status,
    Models.PluginReleaseIncompatibility Reason,
    string Message);
