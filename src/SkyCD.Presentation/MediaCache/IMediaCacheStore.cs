using System;
using System.Collections.Generic;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Durable host-owned storage for plugin extraction results. Records are keyed by source fingerprint
/// plus plugin, capability and schema version, keep their payloads after the source is disconnected or
/// the producing plugin is disabled or uninstalled, and never touch catalog user annotations.
/// </summary>
public interface IMediaCacheStore : IDisposable
{
    /// <summary>The bounds applied to writes and to retention passes that do not supply their own.</summary>
    MediaCacheRetentionPolicy Policy { get; }

    MediaCacheRecord? GetRecord(string recordId);

    IReadOnlyList<MediaCacheRecord> GetRecordsForEntry(string entryId);

    IReadOnlyList<MediaCacheRecord> GetRecordsForFingerprint(string fingerprint);

    IReadOnlyList<MediaCacheRecord> GetAllRecords();

    /// <summary>
    /// Writes a published record. Artifact keys absent from the payload keep their stored bytes, so a
    /// state-only update never drops a preview.
    /// </summary>
    void Save(MediaCachePayload payload);

    bool DeleteRecord(string recordId);

    int DeleteRecordsForEntry(string entryId);

    /// <summary>Records that a cached result was served again, which protects it from LRU eviction.</summary>
    bool Touch(string recordId, DateTimeOffset usedAtUtc);

    /// <summary>Reads artifact bytes by portable reference key; false when the payload is unavailable.</summary>
    bool TryReadArtifact(string recordId, string artifactKey, out byte[] data);

    MediaCacheStatistics GetStatistics();

    /// <summary>Verifies every artifact digest and marks records with unavailable payloads as missing.</summary>
    MediaCacheIntegrityResult VerifyArtifacts();

    /// <summary>
    /// Removes orphaned, expired, oversized and superseded records, then evicts least recently used
    /// records until the total artifact budget is met.
    /// </summary>
    /// <param name="policy">Retention bounds; the store default is used when null.</param>
    /// <param name="entryExists">Reports whether a catalog entry still exists, enabling orphan cleanup.</param>
    /// <param name="nowUtc">Reference time for age-based expiry.</param>
    MediaCacheCleanupResult EnforceRetention(
        MediaCacheRetentionPolicy? policy = null,
        Func<string, bool>? entryExists = null,
        DateTimeOffset? nowUtc = null);

    /// <summary>Opens a staged publication unit, optionally bound to a scan staging session.</summary>
    MediaCacheBatch BeginBatch(string? sessionId = null);

    MediaCacheBatch? GetBatch(string batchId);

    IReadOnlyList<MediaCacheBatch> GetBatches();

    /// <summary>Adds a record to a batch without making it visible to readers.</summary>
    void Stage(string batchId, MediaCachePayload payload);

    IReadOnlyList<MediaCacheRecord> GetStagedRecords(string batchId);

    /// <summary>Makes every staged record of a batch visible in one atomic step.</summary>
    MediaCachePublishResult PublishBatch(string batchId);

    /// <summary>Drops every staged record of a batch; used for cancelled and failed extractions.</summary>
    void DiscardBatch(string batchId);

    int CleanupAbandonedBatches(TimeSpan olderThan);
}
