using System;
using System.Collections.Generic;
using System.Linq;
using Couchbase.Lite;
using Couchbase.Lite.Query;
using SkyCD.Couchbase;
using SkyCD.Couchbase.Mapping;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Durable media cache store backed by the catalog's Couchbase Lite database. Records live in their own
/// collections and artifact bytes are stored as database blobs, so a native catalog backup or copy carries
/// previews and captured metadata with it and references stay valid on any machine.
/// </summary>
public sealed class CouchbaseMediaCacheStore : MediaCacheStoreBase
{
    private const string RecordsCollectionName = "media_cache_records";
    private const string StagedCollectionName = "media_cache_staged";
    private const string BatchesCollectionName = "media_cache_batches";
    private const string BlobKeyPrefix = "blob:";
    private const string BatchIdPropertyName = "BatchId";
    private const string SequencePropertyName = "Sequence";

    private readonly Database database;
    private readonly Collection recordsCollection;
    private readonly Collection stagedCollection;
    private readonly Collection batchesCollection;

    public CouchbaseMediaCacheStore(Database database, MediaCacheRetentionPolicy? defaultPolicy = null)
        : base(defaultPolicy)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));

        recordsCollection = GetOrCreateCollection(database, RecordsCollectionName);
        stagedCollection = GetOrCreateCollection(database, StagedCollectionName);
        batchesCollection = GetOrCreateCollection(database, BatchesCollectionName);
    }

    protected override MediaCacheRecord? ReadRecord(string recordId)
    {
        using var document = recordsCollection.GetDocument(recordId);
        return document.FromDocument<MediaCacheRecord>();
    }

    protected override IReadOnlyList<MediaCacheRecord> ReadAllRecords() =>
        QueryDocumentIds(recordsCollection)
            .Select(id =>
            {
                using var document = recordsCollection.GetDocument(id);
                return document.FromDocument<MediaCacheRecord>();
            })
            .Where(record => record is not null)
            .Select(record => record!)
            .ToArray();

    protected override void WriteRecord(MediaCachePayload payload)
    {
        DatabaseGate.InBatch(database, () =>
        {
            using var existing = recordsCollection.GetDocument(payload.Record.Id);
            using var document = payload.Record.ToMutableDocument(payload.Record.Id);
            CopyArtifactsInto(document, payload, existing);
            recordsCollection.Save(document);
        });
    }

    protected override void RemoveRecord(string recordId)
    {
        using var document = recordsCollection.GetDocument(recordId);
        if (document is not null)
        {
            recordsCollection.Delete(document);
        }
    }

    protected override bool ReadArtifactBytes(string recordId, string artifactKey, out byte[] data)
    {
        using var document = recordsCollection.GetDocument(recordId);
        var content = document?.GetBlob(BlobKeyPrefix + artifactKey)?.Content;
        if (content is null)
        {
            data = [];
            return false;
        }

        data = content;
        return true;
    }

    protected override MediaCacheBatch? ReadBatch(string batchId)
    {
        using var document = batchesCollection.GetDocument(batchId);
        return document.FromDocument<MediaCacheBatch>();
    }

    protected override void WriteBatch(MediaCacheBatch batch)
    {
        using var document = batch.ToMutableDocument(batch.BatchId);
        batchesCollection.Save(document);
    }

    protected override IReadOnlyList<MediaCacheBatch> ReadAllBatches() =>
        QueryDocumentIds(batchesCollection)
            .Select(id =>
            {
                using var document = batchesCollection.GetDocument(id);
                return document.FromDocument<MediaCacheBatch>();
            })
            .Where(batch => batch is not null)
            .Select(batch => batch!)
            .ToArray();

    protected override void RemoveBatch(string batchId)
    {
        DatabaseGate.InBatch(database, () =>
        {
            using var document = batchesCollection.GetDocument(batchId);
            if (document is not null)
            {
                batchesCollection.Delete(document);
            }

            RemoveStagedDocuments(batchId);
        });
    }

    protected override IReadOnlyList<MediaCachePayload> ReadStagedPayloads(string batchId)
    {
        return DatabaseGate.Run(database, () =>
        {
            using var query = QueryBuilder
                .Select(SelectResult.Expression(Meta.ID))
                .From(DataSource.Collection(stagedCollection))
                .Where(Expression.Property(BatchIdPropertyName).EqualTo(Expression.String(batchId)))
                .OrderBy(Ordering.Property(SequencePropertyName).Ascending());

            using var results = query.Execute();
            var payloads = new List<MediaCachePayload>();

            foreach (var row in results)
            {
                var stagedId = row.GetString("id");
                if (string.IsNullOrEmpty(stagedId))
                {
                    continue;
                }

                // Blobs are read from the stored document so their content stays resolvable.
                using var stagedDocument = stagedCollection.GetDocument(stagedId);
                if (stagedDocument is null)
                {
                    continue;
                }

                var record = stagedDocument.FromDocument<MediaCacheRecord>();
                if (record is null)
                {
                    continue;
                }

                payloads.Add(new MediaCachePayload(record, ReadBlobs(stagedDocument, record)));
            }

            return payloads;
        });
    }

    protected override void WriteStagedRecord(string batchId, MediaCachePayload payload)
    {
        var stagedId = $"{batchId}:{payload.Record.Id}";

        DatabaseGate.InBatch(database, () =>
        {
            using var existingDocument = stagedCollection.GetDocument(stagedId)
                                         ?? recordsCollection.GetDocument(payload.Record.Id);
            using var document = payload.Record.ToMutableDocument(stagedId);
            document.SetString(BatchIdPropertyName, batchId);
            document.SetLong(SequencePropertyName, DateTimeOffset.UtcNow.Ticks);
            CopyArtifactsInto(document, payload, existingDocument);
            stagedCollection.Save(document);
        });
    }

    protected override void RemoveStagedRecords(string batchId)
    {
        DatabaseGate.InBatch(database, () => RemoveStagedDocuments(batchId));
    }

    protected override void CompactStorage() =>
        DatabaseGate.Run(database, () => database.PerformMaintenance(MaintenanceType.Compact));

    private void RemoveStagedDocuments(string batchId)
    {
        using var query = QueryBuilder
            .Select(SelectResult.Expression(Meta.ID))
            .From(DataSource.Collection(stagedCollection))
            .Where(Expression.Property(BatchIdPropertyName).EqualTo(Expression.String(batchId)));

        using var results = query.Execute();
        foreach (var row in results)
        {
            var stagedId = row.GetString("id");
            if (string.IsNullOrEmpty(stagedId))
            {
                continue;
            }

            using var document = stagedCollection.GetDocument(stagedId);
            if (document is not null)
            {
                stagedCollection.Delete(document);
            }
        }
    }

    private static IReadOnlyList<string> QueryDocumentIds(Collection collection)
    {
        using var query = QueryBuilder
            .Select(SelectResult.Expression(Meta.ID))
            .From(DataSource.Collection(collection));

        return DatabaseGate.Run(collection.Database, () =>
        {
            using var results = query.Execute();
            var ids = new List<string>();
            foreach (var row in results)
            {
                var id = row.GetString("id");
                if (!string.IsNullOrEmpty(id))
                {
                    ids.Add(id);
                }
            }

            return ids;
        });
    }

    private static Collection GetOrCreateCollection(Database database, string collectionName) =>
        database.GetCollection(collectionName, Collection.DefaultScopeName)
        ?? database.CreateCollection(collectionName, Collection.DefaultScopeName);

    /// <summary>
    /// Writes referenced artifacts from the payload and carries over stored bytes for references the
    /// payload omits, so state-only updates keep previews available offline.
    /// </summary>
    private static void CopyArtifactsInto(MutableDocument document, MediaCachePayload payload, Document? existing)
    {
        foreach (var reference in payload.Record.Artifacts)
        {
            var blobKey = BlobKeyPrefix + reference.Key;
            if (payload.Artifacts.TryGetValue(reference.Key, out var bytes))
            {
                document.SetBlob(blobKey, new Blob(reference.ContentType, bytes));
                continue;
            }

            if (existing?.GetBlob(blobKey) is { } storedBlob)
            {
                document.SetBlob(blobKey, storedBlob);
            }
        }
    }

    private static Dictionary<string, byte[]> ReadBlobs(Document document, MediaCacheRecord record)
    {
        var artifacts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var reference in record.Artifacts)
        {
            if (document.GetBlob(BlobKeyPrefix + reference.Key)?.Content is { } content)
            {
                artifacts[reference.Key] = content;
            }
        }

        return artifacts;
    }
}
