using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Presentation.MediaCache.Exceptions;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Shared cache lifecycle logic: bounded writes, staged publication, integrity verification and
/// retention. Storage backends only supply record, artifact and batch primitives.
/// </summary>
public abstract class MediaCacheStoreBase : IMediaCacheStore
{
    private readonly MediaCacheRetentionPolicy policy;
    private bool isDisposed;

    protected MediaCacheStoreBase(MediaCacheRetentionPolicy? policy = null)
    {
        this.policy = policy ?? new MediaCacheRetentionPolicy();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(this.policy.MaxTotalArtifactBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(this.policy.MaxArtifactBytesPerRecord);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(this.policy.MaxRecordsPerEntry);
    }

    /// <summary>The bounds applied to writes and to retention passes that do not supply their own.</summary>
    public MediaCacheRetentionPolicy Policy => policy;

    protected abstract MediaCacheRecord? ReadRecord(string recordId);

    protected abstract IReadOnlyList<MediaCacheRecord> ReadAllRecords();

    /// <summary>Writes a published record, keeping stored bytes for artifact keys the payload omits.</summary>
    protected abstract void WriteRecord(MediaCachePayload payload);

    protected abstract void RemoveRecord(string recordId);

    protected abstract bool ReadArtifactBytes(string recordId, string artifactKey, out byte[] data);

    protected abstract MediaCacheBatch? ReadBatch(string batchId);

    protected abstract void WriteBatch(MediaCacheBatch batch);

    protected abstract IReadOnlyList<MediaCachePayload> ReadStagedPayloads(string batchId);

    protected abstract void WriteStagedRecord(string batchId, MediaCachePayload payload);

    protected abstract void RemoveStagedRecords(string batchId);

    /// <summary>Reclaims storage left behind by deleted records; a no-op for backends without one.</summary>
    protected abstract void CompactStorage();

    protected void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(isDisposed, this);

    public MediaCacheRecord? GetRecord(string recordId)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);

        return ReadRecord(recordId);
    }

    public IReadOnlyList<MediaCacheRecord> GetRecordsForEntry(string entryId)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);

        return ReadAllRecords()
            .Where(record => string.Equals(record.EntryId, entryId, StringComparison.Ordinal))
            .ToArray();
    }

    public IReadOnlyList<MediaCacheRecord> GetRecordsForFingerprint(string fingerprint)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);

        return ReadAllRecords()
            .Where(record => string.Equals(record.Fingerprint, fingerprint, StringComparison.Ordinal))
            .ToArray();
    }

    public IReadOnlyList<MediaCacheRecord> GetAllRecords()
    {
        ThrowIfDisposed();

        return ReadAllRecords();
    }

    public void Save(MediaCachePayload payload)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(payload);

        ValidateForWrite(payload.Record);
        WriteRecord(payload);
    }

    public bool DeleteRecord(string recordId)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);

        if (ReadRecord(recordId) is null)
        {
            return false;
        }

        RemoveRecord(recordId);
        return true;
    }

    public int DeleteRecordsForEntry(string entryId)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);

        var records = GetRecordsForEntry(entryId);
        foreach (var record in records)
        {
            RemoveRecord(record.Id);
        }

        return records.Count;
    }

    public bool Touch(string recordId, DateTimeOffset usedAtUtc)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);

        var record = ReadRecord(recordId);
        if (record is null)
        {
            return false;
        }

        if (record.LastUsedAtUtc >= usedAtUtc)
        {
            return true;
        }

        record.LastUsedAtUtc = usedAtUtc;
        WriteRecord(new MediaCachePayload(record));
        return true;
    }

    public bool TryReadArtifact(string recordId, string artifactKey, out byte[] data)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactKey);

        return ReadArtifactBytes(recordId, artifactKey, out data);
    }

    public MediaCacheStatistics GetStatistics()
    {
        ThrowIfDisposed();

        var records = ReadAllRecords();
        return new MediaCacheStatistics(
            records.Count,
            records.Select(record => record.EntryId).Distinct(StringComparer.Ordinal).Count(),
            records.Count(record => record.State == MediaCacheRecordState.Captured),
            records.Count(record => record.State == MediaCacheRecordState.Stale),
            records.Count(record => record.State == MediaCacheRecordState.Failed),
            records.Count(record => record.State == MediaCacheRecordState.Unsupported),
            records.Count(record => record.State == MediaCacheRecordState.Missing),
            records.Sum(record => record.ArtifactBytes),
            records.Sum(record => record.MetadataBytes));
    }

    public MediaCacheIntegrityResult VerifyArtifacts()
    {
        ThrowIfDisposed();

        var verified = 0;
        var missing = 0;
        var corrupted = 0;
        var missingIds = new List<string>();

        foreach (var record in ReadAllRecords())
        {
            if (record.Artifacts.Count == 0)
            {
                verified++;
                continue;
            }

            var unavailable = false;
            foreach (var reference in record.Artifacts)
            {
                if (!ReadArtifactBytes(record.Id, reference.Key, out var data))
                {
                    unavailable = true;
                    continue;
                }

                if (data.LongLength != reference.Length ||
                    !string.Equals(MediaCacheHashing.Sha256Hex(data), reference.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    unavailable = true;
                    corrupted++;
                }
            }

            if (!unavailable)
            {
                verified++;
                if (record.State == MediaCacheRecordState.Missing)
                {
                    // Artifacts were restored from a backup bundle or a successful re-extraction.
                    record.State = MediaCacheRecordState.Captured;
                    WriteRecord(new MediaCachePayload(record));
                }

                continue;
            }

            missing++;
            missingIds.Add(record.Id);
            if (record.State != MediaCacheRecordState.Missing)
            {
                record.State = MediaCacheRecordState.Missing;
                WriteRecord(new MediaCachePayload(record));
            }
        }

        return new MediaCacheIntegrityResult(verified, missing, corrupted, missingIds);
    }

    public MediaCacheCleanupResult EnforceRetention(
        MediaCacheRetentionPolicy? policy = null,
        Func<string, bool>? entryExists = null,
        DateTimeOffset? nowUtc = null)
    {
        ThrowIfDisposed();

        policy ??= this.policy;
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var remaining = ReadAllRecords().ToList();
        var removedIds = new HashSet<string>(StringComparer.Ordinal);
        long freedBytes = 0;
        var counters = new RemovalCounters();

        void Remove(MediaCacheRecord record, RemovalReason reason)
        {
            if (!removedIds.Add(record.Id))
            {
                return;
            }

            RemoveRecord(record.Id);
            freedBytes += record.ArtifactBytes;
            counters.Add(reason);
        }

        if (policy.RemoveOrphanedRecords && entryExists is not null)
        {
            foreach (var record in remaining.Where(record => !entryExists(record.EntryId)).ToList())
            {
                Remove(record, RemovalReason.Orphaned);
                remaining.Remove(record);
            }
        }

        if (policy.MaxAge is { } maxAge)
        {
            foreach (var record in remaining.Where(record => now - record.CapturedAtUtc > maxAge).ToList())
            {
                Remove(record, RemovalReason.Expired);
                remaining.Remove(record);
            }
        }

        foreach (var record in remaining
                     .Where(record => record.ArtifactBytes > policy.MaxArtifactBytesPerRecord)
                     .ToList())
        {
            Remove(record, RemovalReason.Oversized);
            remaining.Remove(record);
        }

        foreach (var group in remaining.GroupBy(record => record.EntryId, StringComparer.Ordinal))
        {
            var excess = group.Count() - policy.MaxRecordsPerEntry;
            if (excess <= 0)
            {
                continue;
            }

            foreach (var record in OrderForEviction(group).Take(excess).ToList())
            {
                Remove(record, RemovalReason.EntryCap);
                remaining.Remove(record);
            }
        }

        var totalBytes = MediaCacheCleanupResult.SumArtifactBytes(remaining);
        while (totalBytes > policy.MaxTotalArtifactBytes && remaining.Count > 0)
        {
            var victim = OrderForEviction(remaining).FirstOrDefault();
            if (victim is null)
            {
                break;
            }

            Remove(victim, RemovalReason.Evicted);
            remaining.Remove(victim);
            totalBytes -= victim.ArtifactBytes;
        }

        if (policy.CompactAfterCleanup && removedIds.Count > 0)
        {
            CompactStorage();
        }

        return new MediaCacheCleanupResult(
            counters.Orphaned,
            counters.Expired,
            counters.Oversized,
            counters.EntryCap,
            counters.Evicted,
            freedBytes,
            remaining.Count,
            MediaCacheCleanupResult.SumArtifactBytes(remaining));
    }

    public MediaCacheBatch BeginBatch(string? sessionId = null)
    {
        ThrowIfDisposed();

        var batch = new MediaCacheBatch
        {
            BatchId = $"mediacache-{Guid.NewGuid():N}",
            SessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            RecordCount = 0
        };

        WriteBatch(batch);
        return batch;
    }

    public MediaCacheBatch? GetBatch(string batchId)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);

        return ReadBatch(batchId);
    }

    public IReadOnlyList<MediaCacheBatch> GetBatches()
    {
        ThrowIfDisposed();

        return ReadAllBatches();
    }

    public void Stage(string batchId, MediaCachePayload payload)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
        ArgumentNullException.ThrowIfNull(payload);

        RequireOpenBatch(batchId);
        ValidateForWrite(payload.Record);
        WriteStagedRecord(batchId, payload);
    }

    public IReadOnlyList<MediaCacheRecord> GetStagedRecords(string batchId)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
        if (ReadBatch(batchId) is null)
        {
            throw new MediaCacheBatchNotFoundException(batchId);
        }

        return ReadStagedPayloads(batchId).Select(payload => payload.Record).ToArray();
    }

    public MediaCachePublishResult PublishBatch(string batchId)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);

        var batch = ReadBatch(batchId) ?? throw new MediaCacheBatchNotFoundException(batchId);
        if (batch.IsDiscarded)
        {
            throw new MediaCacheBatchDiscardedException(batchId);
        }

        if (batch.IsPublished)
        {
            throw new MediaCacheBatchAlreadyPublishedException(batchId);
        }

        var staged = ReadStagedPayloads(batchId);
        foreach (var payload in staged)
        {
            WriteRecord(payload);
        }

        RemoveStagedRecords(batchId);

        batch.IsPublished = true;
        batch.PublishedAtUtc = DateTimeOffset.UtcNow;
        batch.RecordCount = staged.Count;
        WriteBatch(batch);

        return new MediaCachePublishResult(
            batchId,
            staged.Count,
            staged.Sum(payload => payload.Record.Artifacts.Count),
            staged.Sum(payload => payload.Record.ArtifactBytes),
            batch.PublishedAtUtc.Value);
    }

    public void DiscardBatch(string batchId)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);

        var batch = ReadBatch(batchId) ?? throw new MediaCacheBatchNotFoundException(batchId);
        if (batch.IsPublished)
        {
            throw new MediaCacheBatchAlreadyPublishedException(batchId);
        }

        RemoveStagedRecords(batchId);

        batch.IsDiscarded = true;
        batch.RecordCount = 0;
        WriteBatch(batch);
    }

    public int CleanupAbandonedBatches(TimeSpan olderThan)
    {
        ThrowIfDisposed();

        var cutoff = DateTimeOffset.UtcNow - olderThan;
        var abandoned = ReadAllBatches()
            .Where(batch => batch.IsPublished || batch.IsDiscarded || batch.CreatedAtUtc < cutoff)
            .ToList();

        foreach (var batch in abandoned)
        {
            if (!batch.IsPublished)
            {
                RemoveStagedRecords(batch.BatchId);
            }

            RemoveBatch(batch.BatchId);
        }

        return abandoned.Count;
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        DisposeCore();
        GC.SuppressFinalize(this);
    }

    protected abstract IReadOnlyList<MediaCacheBatch> ReadAllBatches();

    protected abstract void RemoveBatch(string batchId);

    protected virtual void DisposeCore()
    {
    }

    private MediaCacheBatch RequireOpenBatch(string batchId)
    {
        var batch = ReadBatch(batchId) ?? throw new MediaCacheBatchNotFoundException(batchId);
        if (batch.IsPublished)
        {
            throw new MediaCacheBatchAlreadyPublishedException(batchId);
        }

        if (batch.IsDiscarded)
        {
            throw new MediaCacheBatchDiscardedException(batchId);
        }

        return batch;
    }

    private void ValidateForWrite(MediaCacheRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.EntryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.Fingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.PluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.Capability);

        record.WithRecalculatedSizes();

        if (record.ArtifactBytes > policy.MaxArtifactBytesPerRecord)
        {
            throw new MediaCacheArtifactTooLargeException(
                record.Id,
                record.ArtifactBytes,
                policy.MaxArtifactBytesPerRecord);
        }

        foreach (var reference in record.Artifacts)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reference.Key);
            ArgumentException.ThrowIfNullOrWhiteSpace(reference.ContentType);
        }
    }

    private static IEnumerable<MediaCacheRecord> OrderForEviction(IEnumerable<MediaCacheRecord> records) => records
        .OrderBy(record => MediaCacheRetentionPolicy.GetEvictionRank(record.State))
        .ThenBy(record => record.LastUsedAtUtc)
        .ThenBy(record => record.Id, StringComparer.Ordinal);

    private enum RemovalReason
    {
        Orphaned,
        Expired,
        Oversized,
        EntryCap,
        Evicted
    }

    private sealed class RemovalCounters
    {
        public int Orphaned { get; private set; }
        public int Expired { get; private set; }
        public int Oversized { get; private set; }
        public int EntryCap { get; private set; }
        public int Evicted { get; private set; }

        public void Add(RemovalReason reason)
        {
            switch (reason)
            {
                case RemovalReason.Orphaned: Orphaned++; break;
                case RemovalReason.Expired: Expired++; break;
                case RemovalReason.Oversized: Oversized++; break;
                case RemovalReason.EntryCap: EntryCap++; break;
                default: Evicted++; break;
            }
        }
    }
}
