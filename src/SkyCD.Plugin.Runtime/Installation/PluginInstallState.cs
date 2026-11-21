using System;

namespace SkyCD.Plugin.Runtime.Installation;

/// <summary>
/// Catalog-owned install state exposed to the marketplace UI: active version, pending restart operation and last failure.
/// </summary>
public sealed record PluginInstallState
{
    public required string PluginId { get; init; }

    public PluginInstallOperationStatus Status { get; init; } = PluginInstallOperationStatus.NotInstalled;

    /// <summary>Version currently present in the plugin discovery root, or <c>null</c> when the plugin is not active.</summary>
    public string? ActiveVersion { get; init; }

    /// <summary>Version that activates on the next restart, or the version that failed during the last operation.</summary>
    public string? PendingVersion { get; init; }

    /// <summary>Version restored by the last rollback, or <c>null</c>.</summary>
    public string? RolledBackFromVersion { get; init; }

    public string? SourceUrl { get; init; }

    public string? PackageId { get; init; }

    /// <summary>True when the pending operation is a fresh install rather than an update.</summary>
    public bool IsFreshInstall { get; init; }

    public DateTimeOffset? PendingSince { get; init; }

    public DateTimeOffset? InstalledAt { get; init; }

    public PluginInstallFailureReason FailureReason { get; init; } = PluginInstallFailureReason.None;

    public string? Error { get; init; }

    public bool RestartRequired => Status == PluginInstallOperationStatus.PendingRestart;

    public static PluginInstallState NotInstalled(string pluginId) => new() { PluginId = pluginId };

    /// <summary>Projection of the operation state used by the plugin layout and staging validators.</summary>
    public static PluginInstallState From(PluginInstallOperation operation) => new()
    {
        PluginId = operation.PluginId,
        Status = operation.Status,
        ActiveVersion = operation.PreviousVersion ?? (operation.Status is PluginInstallOperationStatus.Installed
            ? operation.Version
            : null),
        PendingVersion = operation.Status is PluginInstallOperationStatus.PendingRestart
            or PluginInstallOperationStatus.Failed
            or PluginInstallOperationStatus.RolledBack
            ? operation.Version
            : null,
        RolledBackFromVersion = operation.Status == PluginInstallOperationStatus.RolledBack
            ? operation.PreviousVersion
            : null,
        SourceUrl = operation.SourceUrl,
        PackageId = operation.PackageId,
        IsFreshInstall = operation.PreviousVersion is null,
        PendingSince = operation.StagedAt,
        InstalledAt = operation.InstalledAt,
        FailureReason = operation.FailureReason,
        Error = operation.Error
    };
}
