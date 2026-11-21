using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.Runtime.Marketplace.Models;

/// <summary>Human-readable status details for one publisher manifest or for the catalog listing itself. The
/// record's value equality is the change-detection key a UI can use to decide whether a status needs to be
/// reported again.</summary>
/// <param name="Code">Technical code, for example <c>HostTooOld</c> or <c>UnsupportedSchema</c>.</param>
/// <param name="Message">English, user-readable explanation.</param>
public sealed record PluginSourceStatusDetail(string Code, string Message)
{
    /// <summary>Status details of the catalog listing document.</summary>
    public static PluginSourceStatusDetail ForCatalog(PluginCatalogStatus status)
    {
        return status switch
        {
            PluginCatalogStatus.Loaded => new PluginSourceStatusDetail("Loaded", "Catalog loaded."),
            PluginCatalogStatus.NotConfigured => new PluginSourceStatusDetail("NotConfigured",
                "No plugin catalog endpoint is configured."),
            _ => new PluginSourceStatusDetail("Offline",
                "The plugin catalog could not be reached. Showing the last known sources.")
        };
    }

    public static PluginSourceStatusDetail Create(PluginSourceStatus status, int unsupportedSchemaVersion)
    {
        return status switch
        {
            PluginSourceStatus.Loaded => new PluginSourceStatusDetail("Loaded", "Manifest loaded."),
            PluginSourceStatus.Offline => new PluginSourceStatusDetail("Offline",
                "The source could not be reached. Showing the last known result."),
            PluginSourceStatus.UnsupportedSchema => new PluginSourceStatusDetail("UnsupportedSchema",
                $"The manifest uses schema version {unsupportedSchemaVersion}, which this version of SkyCD does not support."),
            PluginSourceStatus.Malformed => new PluginSourceStatusDetail("Malformed",
                "The manifest is not a valid publisher manifest."),
            PluginSourceStatus.Rejected => new PluginSourceStatusDetail("Rejected",
                "The manifest URL must be an absolute HTTPS address."),
            _ => new PluginSourceStatusDetail("Unknown", "The source did not report a recognized status.")
        };
    }
}

/// <summary>
/// Outcome of one publisher manifest. Instances are immutable: a refresh replaces the snapshot of a source
/// instead of editing it, so whatever a downstream issue holds keeps describing the source it came from.
/// </summary>
public sealed record PluginSourceSnapshot
{
    /// <summary>Absolute HTTPS URL of the publisher manifest, or a placeholder for a rejected catalog entry.</summary>
    public required Uri ManifestUri { get; init; }

    public required PluginSourceStatus Status { get; init; }

    public required PluginSourceStatusDetail Detail { get; init; }

    /// <summary>Raw catalog text for entries that were rejected before a URL could be formed.</summary>
    public string? RawEntry { get; init; }

    /// <summary>Publisher name from the manifest, when it could be read.</summary>
    public string? Publisher { get; init; }

    /// <summary>Publisher site from the manifest, when it could be read.</summary>
    public Uri? ProjectUrl { get; init; }

    /// <summary>Packages contributed by this manifest, newest first; empty for sources without a result.</summary>
    public IReadOnlyList<MarketplacePackage> Packages { get; init; } = [];

    /// <summary>Whether the packages come from the last known good result instead of this refresh.</summary>
    public bool IsStale { get; init; }

    /// <summary>Whether this source needs attention in a partial-failure summary.</summary>
    public bool IsFailing => Status is not (PluginSourceStatus.Loaded or PluginSourceStatus.Rejected);

    public override string ToString()
    {
        return $"{ManifestUri} -> {Status}";
    }
}
