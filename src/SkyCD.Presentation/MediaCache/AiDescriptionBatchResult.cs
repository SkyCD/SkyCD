using System.Collections.Generic;
using System.Linq;

namespace SkyCD.Presentation.MediaCache;

/// <summary>The disposition of one explicitly selected entry in a batch generation pass.</summary>
public enum AiDescriptionBatchItemOutcome
{
    Generated = 0,
    Skipped = 1,
    Failed = 2,
    Cancelled = 3
}

/// <summary>One per-entry batch result, including a sanitized failure reason.</summary>
public sealed record AiDescriptionBatchItemResult(
    string EntryId,
    string Name,
    AiDescriptionBatchItemOutcome Outcome,
    string? Error = null);

/// <summary>A progressive progress report emitted after each batch item settles.</summary>
public sealed record AiDescriptionBatchProgress(int Completed, int Total, AiDescriptionBatchItemResult Item);

/// <summary>Final batch output with deterministic per-entry ordering.</summary>
public sealed record AiDescriptionBatchResult(IReadOnlyList<AiDescriptionBatchItemResult> Items)
{
    public int GeneratedCount => Items.Count(item => item.Outcome == AiDescriptionBatchItemOutcome.Generated);

    public int SkippedCount => Items.Count(item => item.Outcome == AiDescriptionBatchItemOutcome.Skipped);

    public int FailedCount => Items.Count(item => item.Outcome == AiDescriptionBatchItemOutcome.Failed);

    public int CancelledCount => Items.Count(item => item.Outcome == AiDescriptionBatchItemOutcome.Cancelled);
}
