namespace SkyCD.Plugin.Runtime.Installation;

/// <summary>
/// Outcome of staging, activating or cancelling a catalog plugin operation.
/// </summary>
public sealed record PluginInstallResult
{
    private PluginInstallResult()
    {
    }

    public required bool IsSuccess { get; init; }

    public required PluginInstallOperationStatus Status { get; init; }

    public PluginInstallFailureReason FailureReason { get; init; } = PluginInstallFailureReason.None;

    /// <summary>Safe operator-facing description; never contains package or download payload data.</summary>
    public string? Message { get; init; }

    /// <summary>True when a retry of the same staged operation found existing state instead of downloading again.</summary>
    public bool IsIdempotentRetry { get; init; }

    public required PluginInstallState State { get; init; }

    public static PluginInstallResult Success(PluginInstallState state, bool isIdempotentRetry = false) => new()
    {
        IsSuccess = true,
        Status = state.Status,
        State = state,
        IsIdempotentRetry = isIdempotentRetry
    };

    public static PluginInstallResult Failure(PluginInstallFailureReason reason, string message,
        PluginInstallState state) => new()
        {
            IsSuccess = false,
            Status = PluginInstallOperationStatus.Failed,
            FailureReason = reason,
            Message = message,
            State = state
        };
}
