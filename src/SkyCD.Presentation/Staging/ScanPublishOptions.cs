namespace SkyCD.Presentation.Staging;

public sealed class ScanPublishOptions
{
    public bool KeepPartialOnFailureOrCancel { get; init; }
    public string? ErrorSummary { get; init; }
    public bool IsIncomplete { get; init; }
    public int BatchSize { get; init; } = 1000;
    public bool PreserveUserMetadata { get; init; } = true;
}
