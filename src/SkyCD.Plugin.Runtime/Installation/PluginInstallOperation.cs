using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.Runtime.Installation;

/// <summary>
/// Marker of one plugin's active install version plus the pending catalog operation, if any.
/// </summary>
public sealed record PluginInstallOperation
{
    public required string PluginId { get; init; }

    public required PluginInstallOperationStatus Status { get; init; }

    public required string SourceUrl { get; init; }

    public required string PackageId { get; init; }

    /// <summary>Package version of the operation (pending, installed, failed or rolled back).</summary>
    public required string Version { get; init; }

    /// <summary>Active version before the operation; <c>null</c> for a fresh install.</summary>
    public string? PreviousVersion { get; init; }

    /// <summary>Entry assembly simple name resolved from package metadata; informational and used for conflict checks.</summary>
    public string? EntryAssemblyName { get; init; }

    /// <summary>Entry assembly version resolved from package metadata.</summary>
    public string? AssemblyVersion { get; init; }

    public required DateTimeOffset StagedAt { get; init; }

    public DateTimeOffset? InstalledAt { get; init; }

    public PluginInstallFailureReason FailureReason { get; init; } = PluginInstallFailureReason.None;

    public string? Error { get; init; }

    /// <summary>Staging directory holding the validated package content while the operation is pending.</summary>
    public string? StagedPath { get; init; }

    /// <summary>Package-relative path of the entry assembly, resolved from package metadata during staging.</summary>
    public string? EntryAssemblyPath { get; init; }

    /// <summary>True when the operation was read from a receipt written by an existing installation.</summary>
    public bool IsCatalogOwned { get; init; } = true;

    /// <summary>Plugin ids discovered from package metadata; used to detect packages that claim another plugin.</summary>
    public IReadOnlyList<string> ClaimedPluginIds { get; init; } = [];

    public static bool IsSameOperation(PluginInstallOperation? existing, PluginPackageRequest request) =>
        existing is not null &&
        existing.Status == PluginInstallOperationStatus.PendingRestart &&
        string.Equals(existing.SourceUrl, request.serviceIndexUrl.Trim(), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(existing.PackageId, request.NormalizedPackageId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(existing.Version, request.NormalizedVersion, StringComparison.OrdinalIgnoreCase);
}
