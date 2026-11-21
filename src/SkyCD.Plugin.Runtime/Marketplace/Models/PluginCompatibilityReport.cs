using System;

namespace SkyCD.Plugin.Runtime.Marketplace.Models;

/// <summary>Compatibility of one package release with the running host.</summary>
public sealed record PluginCompatibilityReport
{
    private PluginCompatibilityReport(PluginCompatibilityStatus status, PluginReleaseIncompatibility reason,
        string message)
    {
        Status = status;
        Reason = reason;
        Message = message;
    }

    /// <summary>Compatible, incompatible or unknown.</summary>
    public PluginCompatibilityStatus Status { get; init; }

    /// <summary>Technical reason; <see cref="PluginReleaseIncompatibility.None"/> when compatible or unknown.</summary>
    public PluginReleaseIncompatibility Reason { get; init; }

    /// <summary>English explanation; when the status is unknown this is the label shown instead of a verdict.</summary>
    public string Message { get; init; }

    public bool IsCompatible => Status == PluginCompatibilityStatus.Compatible;

    public static PluginCompatibilityReport Eligible()
    {
        return new PluginCompatibilityReport(PluginCompatibilityStatus.Compatible,
            PluginReleaseIncompatibility.None, "Compatible with this SkyCD version.");
    }

    public static PluginCompatibilityReport HostTooOld(Version minHostVersion, Version hostVersion)
    {
        return new PluginCompatibilityReport(PluginCompatibilityStatus.Incompatible,
            PluginReleaseIncompatibility.HostTooOld,
            $"The release requires SkyCD {minHostVersion} or later; this is SkyCD {hostVersion}.");
    }

    public static PluginCompatibilityReport HostTooNew(Version maxHostVersion, Version hostVersion)
    {
        return new PluginCompatibilityReport(PluginCompatibilityStatus.Incompatible,
            PluginReleaseIncompatibility.HostTooNew,
            $"The release supports SkyCD up to {maxHostVersion}; this is SkyCD {hostVersion}.");
    }

    public static PluginCompatibilityReport PlatformMismatch(string platforms, string runtimeIdentifier)
    {
        return new PluginCompatibilityReport(PluginCompatibilityStatus.Incompatible,
            PluginReleaseIncompatibility.PlatformMismatch,
            $"The release is published for {platforms}; this platform is {runtimeIdentifier}.");
    }

    /// <summary>The release passed host and platform rules but the feed was read and does not list the version.</summary>
    public static PluginCompatibilityReport NotPublished(string message)
    {
        return new PluginCompatibilityReport(PluginCompatibilityStatus.Incompatible,
            PluginReleaseIncompatibility.NotPublished, message);
    }

    public static PluginCompatibilityReport PrereleaseExcluded()
    {
        return new PluginCompatibilityReport(PluginCompatibilityStatus.Incompatible,
            PluginReleaseIncompatibility.Prerelease,
            "A prerelease. SkyCD only selects prerelease packages when prereleases are allowed.");
    }

    /// <summary>
    /// Compatibility is unknown when the catalog cannot reach the feed (the version list is the source of
    /// truth for availability) or the feed does not expose a package content resource. Unknown releases are
    /// never selected automatically; they are shown explicitly as unknown.
    /// </summary>
    public static PluginCompatibilityReport Unknown(string reason)
    {
        return new PluginCompatibilityReport(PluginCompatibilityStatus.Unknown,
            PluginReleaseIncompatibility.None, $"Compatibility unknown: {reason}");
    }

    /// <summary>Restores a verdict stored with a cached manifest.</summary>
    public static PluginCompatibilityReport FromCache(PluginCompatibilityStatus status,
        PluginReleaseIncompatibility reason, string message)
    {
        if (status == PluginCompatibilityStatus.Unknown)
        {
            return new PluginCompatibilityReport(status, PluginReleaseIncompatibility.None, message);
        }

        return new PluginCompatibilityReport(status, reason, message);
    }
}
