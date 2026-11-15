using System;

namespace SkyCD.Presentation.MediaCache;

/// <summary>Outcome of publishing one staged media cache batch.</summary>
public sealed record MediaCachePublishResult(
    string BatchId,
    int PublishedRecordCount,
    int ArtifactCount,
    long ArtifactBytes,
    DateTimeOffset PublishedAtUtc);
