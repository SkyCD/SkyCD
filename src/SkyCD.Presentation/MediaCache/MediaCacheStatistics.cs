namespace SkyCD.Presentation.MediaCache;

/// <summary>Snapshot of cache occupancy, used for bounded storage reporting and retention decisions.</summary>
public sealed record MediaCacheStatistics(
    int RecordCount,
    int EntryCount,
    int CapturedCount,
    int StaleCount,
    int FailedCount,
    int UnsupportedCount,
    int MissingCount,
    long ArtifactBytes,
    long MetadataBytes);
