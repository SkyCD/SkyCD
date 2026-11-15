using System;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Bounds for the offline media cache. Captured payloads are kept indefinitely by default so that
/// previews and metadata survive disconnected media; size and per-entry bounds are enforced by
/// evicting the least recently used records first.
/// </summary>
/// <param name="MaxTotalArtifactBytes">Total artifact budget for the whole cache.</param>
/// <param name="MaxArtifactBytesPerRecord">Per-record artifact bound; larger results are stored as failures.</param>
/// <param name="MaxRecordsPerEntry">Superseded and stale records beyond this count are pruned per entry.</param>
/// <param name="MaxAge">Optional maximum record age; null keeps records until size bounds force eviction.</param>
/// <param name="RemoveOrphanedRecords">Remove records whose catalog entry no longer exists.</param>
/// <param name="CompactAfterCleanup">Reclaim orphaned artifact storage after a cleanup pass.</param>
public sealed record MediaCacheRetentionPolicy(
    long MaxTotalArtifactBytes = 256 * 1024 * 1024,
    long MaxArtifactBytesPerRecord = 4 * 1024 * 1024,
    int MaxRecordsPerEntry = 16,
    TimeSpan? MaxAge = null,
    bool RemoveOrphanedRecords = true,
    bool CompactAfterCleanup = true)
{
    /// <summary>Eviction order: records without a usable payload, then the least recently used.</summary>
    public static int GetEvictionRank(MediaCacheRecordState state) => state switch
    {
        MediaCacheRecordState.Failed => 0,
        MediaCacheRecordState.Unsupported => 0,
        MediaCacheRecordState.Missing => 1,
        MediaCacheRecordState.Stale => 2,
        _ => 3
    };
}
