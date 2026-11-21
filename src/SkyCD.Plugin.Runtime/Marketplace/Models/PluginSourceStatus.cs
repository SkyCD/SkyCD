namespace SkyCD.Plugin.Runtime.Marketplace.Models;

/// <summary>State of the catalog listing source itself (the document that lists publisher manifests).</summary>
public enum PluginCatalogStatus
{
    /// <summary>The catalog was read this time.</summary>
    Loaded,

    /// <summary>No catalog endpoint was configured, so there are no sources.</summary>
    NotConfigured,

    /// <summary>The catalog endpoint could not be reached; the last known good source list is used.</summary>
    Offline
}

/// <summary>State of one publisher manifest.</summary>
public enum PluginSourceStatus
{
    /// <summary>The manifest was read and its schema version is supported.</summary>
    Loaded,

    /// <summary>The manifest could not be reached; the last known good result is used.</summary>
    Offline,

    /// <summary>The manifest declares a schema version this host does not support.</summary>
    UnsupportedSchema,

    /// <summary>The manifest is not valid JSON or does not follow its declared schema version.</summary>
    Malformed,

    /// <summary>The manifest URL is not an allowed HTTPS address; it was never fetched.</summary>
    Rejected
}

/// <summary>Why a package release cannot be selected automatically.</summary>
public enum PluginReleaseIncompatibility
{
    /// <summary>The release is eligible.</summary>
    None,

    /// <summary>The running host is older than the release's minimum host version.</summary>
    HostTooOld,

    /// <summary>The running host is newer than the release's maximum host version.</summary>
    HostTooNew,

    /// <summary>The running platform is not among the release's platforms.</summary>
    PlatformMismatch,

    /// <summary>The feed does not list the release version.</summary>
    NotPublished,

    /// <summary>The release is a prerelease and prereleases are excluded from automatic selection.</summary>
    Prerelease
}
