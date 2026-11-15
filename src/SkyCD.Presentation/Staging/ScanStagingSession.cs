using System;

namespace SkyCD.Presentation.Staging;

public sealed class ScanStagingSession
{
    public string SessionId { get; set; } = string.Empty;
    public string GenerationId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public ScanStagingSessionStatus Status { get; set; } = ScanStagingSessionStatus.Active;
    public string? SourcePathOrAddress { get; set; }
    public string? TargetParentId { get; set; }
    public string? MediaName { get; set; }
    public string? ErrorSummary { get; set; }
    public bool IsIncomplete { get; set; }
    public int DocumentsCount { get; set; }
}
