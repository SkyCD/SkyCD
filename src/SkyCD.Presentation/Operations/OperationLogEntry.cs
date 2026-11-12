using System;

namespace SkyCD.Presentation.Operations;

public sealed record OperationLogEntry
{
    public OperationLogEntry(
        OperationLogLevel level,
        string message,
        string? stage = null,
        string? itemPath = null,
        string? details = null,
        DateTimeOffset? timestamp = null)
    {
        Timestamp = timestamp ?? DateTimeOffset.Now;
        Level = level;
        Stage = string.IsNullOrWhiteSpace(stage) ? null : SecretSanitizer.Sanitize(stage);
        ItemPath = string.IsNullOrWhiteSpace(itemPath) ? null : SecretSanitizer.Sanitize(itemPath);
        Message = SecretSanitizer.Sanitize(message);
        Details = string.IsNullOrWhiteSpace(details) ? null : SecretSanitizer.Sanitize(details);
    }

    public DateTimeOffset Timestamp { get; init; }
    public OperationLogLevel Level { get; init; }
    public string Message { get; init; }
    public string? Stage { get; init; }
    public string? ItemPath { get; init; }
    public string? Details { get; init; }

    public string FormattedTimestamp => Timestamp.ToString("HH:mm:ss.fff");

    public string DisplayText
    {
        get
        {
            var stagePrefix = string.IsNullOrEmpty(Stage) ? string.Empty : $"[{Stage}] ";
            var itemSuffix = string.IsNullOrEmpty(ItemPath) ? string.Empty : $" ({ItemPath})";
            var detailsSuffix = string.IsNullOrEmpty(Details) ? string.Empty : $" - {Details}";
            return $"[{FormattedTimestamp}] [{Level}] {stagePrefix}{Message}{itemSuffix}{detailsSuffix}";
        }
    }
}
