using System;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// One staged media cache publication unit. A batch is bound to the scan staging session that produced
/// it so extraction results become visible exactly when the scan is published, and disappear when it is
/// cancelled or abandoned.
/// </summary>
public sealed class MediaCacheBatch
{
    public string BatchId { get; set; } = string.Empty;

    /// <summary>The scan staging session id this batch belongs to, when extraction ran during a scan.</summary>
    public string? SessionId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? PublishedAtUtc { get; set; }

    public bool IsPublished { get; set; }

    public bool IsDiscarded { get; set; }

    public int RecordCount { get; set; }
}
