using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// In-process media cache store. Used when no catalog database is available and by tests that need
/// the same lifecycle semantics without storage dependencies.
/// </summary>
public sealed class InMemoryMediaCacheStore : MediaCacheStoreBase
{
    private readonly Dictionary<string, MediaCacheRecord> records = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, byte[]>> artifactsByRecordId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MediaCacheBatch> batches = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, StagedPayload>> stagedByBatchId = new(StringComparer.Ordinal);
    private readonly object gate = new();

    public InMemoryMediaCacheStore(MediaCacheRetentionPolicy? defaultPolicy = null)
        : base(defaultPolicy)
    {
    }

    protected override MediaCacheRecord? ReadRecord(string recordId)
    {
        lock (gate)
        {
            return records.TryGetValue(recordId, out var record) ? record.Clone() : null;
        }
    }

    protected override IReadOnlyList<MediaCacheRecord> ReadAllRecords()
    {
        lock (gate)
        {
            return records.Values.Select(record => record.Clone()).ToArray();
        }
    }

    protected override void WriteRecord(MediaCachePayload payload)
    {
        lock (gate)
        {
            StoreRecord(payload.Record, payload.Artifacts, artifactsByRecordId);
        }
    }

    protected override void RemoveRecord(string recordId)
    {
        lock (gate)
        {
            records.Remove(recordId);
            artifactsByRecordId.Remove(recordId);
        }
    }

    protected override bool ReadArtifactBytes(string recordId, string artifactKey, out byte[] data)
    {
        lock (gate)
        {
            if (artifactsByRecordId.TryGetValue(recordId, out var artifacts) &&
                artifacts.TryGetValue(artifactKey, out var stored))
            {
                data = (byte[])stored.Clone();
                return true;
            }
        }

        data = [];
        return false;
    }

    protected override MediaCacheBatch? ReadBatch(string batchId)
    {
        lock (gate)
        {
            return batches.TryGetValue(batchId, out var batch) ? batch : null;
        }
    }

    protected override void WriteBatch(MediaCacheBatch batch)
    {
        lock (gate)
        {
            batches[batch.BatchId] = batch;
        }
    }

    protected override IReadOnlyList<MediaCacheBatch> ReadAllBatches()
    {
        lock (gate)
        {
            return batches.Values.ToArray();
        }
    }

    protected override void RemoveBatch(string batchId)
    {
        lock (gate)
        {
            batches.Remove(batchId);
            stagedByBatchId.Remove(batchId);
        }
    }

    protected override IReadOnlyList<MediaCachePayload> ReadStagedPayloads(string batchId)
    {
        lock (gate)
        {
            if (!stagedByBatchId.TryGetValue(batchId, out var staged))
            {
                return [];
            }

            return staged.Values
                .OrderBy(entry => entry.Sequence)
                .Select(entry => new MediaCachePayload(
                    entry.Record.Clone(),
                    entry.Artifacts.ToDictionary(pair => pair.Key, pair => (byte[])pair.Value.Clone(), StringComparer.Ordinal)))
                .ToArray();
        }
    }

    protected override void WriteStagedRecord(string batchId, MediaCachePayload payload)
    {
        lock (gate)
        {
            if (!stagedByBatchId.TryGetValue(batchId, out var staged))
            {
                staged = new Dictionary<string, StagedPayload>(StringComparer.Ordinal);
                stagedByBatchId[batchId] = staged;
            }

            staged.TryGetValue(payload.Record.Id, out var previous);
            var artifacts = previous is null
                ? new Dictionary<string, byte[]>(StringComparer.Ordinal)
                : new Dictionary<string, byte[]>(previous.Artifacts, StringComparer.Ordinal);

            foreach (var (key, bytes) in payload.Artifacts)
            {
                artifacts[key] = (byte[])bytes.Clone();
            }

            staged[payload.Record.Id] = new StagedPayload(
                payload.Record.Clone(),
                artifacts,
                previous?.Sequence + 1 ?? 0);
        }
    }

    protected override void RemoveStagedRecords(string batchId)
    {
        lock (gate)
        {
            stagedByBatchId.Remove(batchId);
        }
    }

    protected override void CompactStorage()
    {
    }

    private void StoreRecord(
        MediaCacheRecord record,
        IReadOnlyDictionary<string, byte[]> provided,
        Dictionary<string, Dictionary<string, byte[]>> artifactsTarget)
    {
        records[record.Id] = record.Clone();

        if (!artifactsTarget.TryGetValue(record.Id, out var stored))
        {
            stored = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            artifactsTarget[record.Id] = stored;
        }

        foreach (var (key, bytes) in provided)
        {
            stored[key] = (byte[])bytes.Clone();
        }

        var referenced = new HashSet<string>(record.Artifacts.Select(artifact => artifact.Key), StringComparer.Ordinal);
        foreach (var key in stored.Keys.Where(key => !referenced.Contains(key)).ToList())
        {
            stored.Remove(key);
        }
    }

    private sealed record StagedPayload(
        MediaCacheRecord Record,
        Dictionary<string, byte[]> Artifacts,
        int Sequence);
}
