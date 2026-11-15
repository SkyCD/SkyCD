using System.Collections.Generic;

namespace SkyCD.Presentation.MediaCache;

/// <summary>Result of a cache retention and orphan cleanup pass.</summary>
public sealed record MediaCacheCleanupResult(
    int OrphanedRecordsRemoved,
    int ExpiredRecordsRemoved,
    int OversizedRecordsRemoved,
    int EntryCapRecordsRemoved,
    int EvictedRecordsRemoved,
    long FreedArtifactBytes,
    int RemainingRecordCount,
    long RemainingArtifactBytes)
{
    public int TotalRemoved =>
        OrphanedRecordsRemoved + ExpiredRecordsRemoved + OversizedRecordsRemoved +
        EntryCapRecordsRemoved + EvictedRecordsRemoved;

    internal static long SumArtifactBytes(IEnumerable<MediaCacheRecord> records)
    {
        long total = 0;
        foreach (var record in records)
        {
            total += record.ArtifactBytes;
        }

        return total;
    }
}
