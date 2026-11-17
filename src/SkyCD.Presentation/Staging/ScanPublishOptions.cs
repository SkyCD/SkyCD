using System.Threading;

namespace SkyCD.Presentation.Staging;

public sealed class ScanPublishOptions
{
    public CancellationToken CancellationToken { get; init; }
    public bool KeepPartialOnFailureOrCancel { get; init; }
    public string? ErrorSummary { get; init; }
    public bool IsIncomplete { get; init; }
    public int BatchSize { get; init; } = 1000;
    public bool PreserveUserMetadata { get; init; } = true;
}
