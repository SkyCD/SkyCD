using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.Runtime.Installation;

/// <summary>
/// Persisted record of a catalog-owned plugin installation. Its presence marks the plugin folder as
/// owned by the catalog installer; folders without a receipt are manual or bundled and are never replaced.
/// </summary>
public sealed record PluginInstallReceipt
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>Schema version of the persisted document; receipts from a newer schema are rejected.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Installed plugin id (the plugin folder name).</summary>
    public required string PluginId { get; init; }

    public required string SourceUrl { get; init; }

    public required string PackageId { get; init; }

    /// <summary>Active package version.</summary>
    public required string Version { get; init; }

    /// <summary>Version replaced by this install, kept for review and rollback auditing.</summary>
    public string? PreviousVersion { get; init; }

    public required string EntryAssemblyPath { get; init; }

    public required string EntryAssemblyName { get; init; }

    public required string AssemblyVersion { get; init; }

    public string? MinHostVersion { get; init; }

    public string? MaxHostVersion { get; init; }

    /// <summary>Plugin ids declared by every plugin assembly in the package.</summary>
    public IReadOnlyList<string> ClaimedPluginIds { get; init; } = [];

    public required DateTimeOffset InstalledAt { get; init; }

    public PluginAssemblyIdentitySnapshot ToIdentity() => new()
    {
        PluginId = PluginId,
        AssemblyName = EntryAssemblyName,
        AssemblyVersion = AssemblyVersion,
        MinHostVersion = MinHostVersion,
        MaxHostVersion = MaxHostVersion,
        EntryAssemblyPath = EntryAssemblyPath
    };

    public PluginInstallState ToState(PluginInstallOperation? pending = null) => new()
    {
        PluginId = PluginId,
        Status = pending?.Status ?? PluginInstallOperationStatus.Installed,
        ActiveVersion = Version,
        PendingVersion = pending?.Version,
        SourceUrl = SourceUrl,
        PackageId = PackageId,
        IsFreshInstall = pending?.PreviousVersion is null,
        PendingSince = pending?.StagedAt,
        InstalledAt = InstalledAt,
        FailureReason = pending?.FailureReason ?? PluginInstallFailureReason.None,
        Error = pending?.Error
    };
}
