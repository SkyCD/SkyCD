using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Couchbase;
using SkyCD.Couchbase.Repository;

namespace SkyCD.Documents.Repository;

/// <summary>
/// Pending captured-entry metadata work, spread over the open catalog database so a queue survives a restart.
/// </summary>
public sealed class CaptureMetadataQueueRepository : RepositoryBase<CaptureMetadataQueueDocument>
{
    /// <summary>Queues an entry unless it is already pending; re-importing never resets its attempt count.</summary>
    public void Enqueue(string entryId, DateTimeOffset enqueuedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        if (Get(entryId) is not null)
        {
            return;
        }

        Save(entryId, new CaptureMetadataQueueDocument
        {
            Id = entryId,
            EntryId = entryId,
            EnqueuedAtUtc = enqueuedAtUtc
        });
    }

    /// <summary>Pending entries oldest first, so a backlog drains in capture order.</summary>
    public IReadOnlyList<CaptureMetadataQueueDocument> GetPending() =>
        GetAll()
            .Where(static item => !string.IsNullOrWhiteSpace(item.EntryId))
            .OrderBy(static item => item.EnqueuedAtUtc)
            .ThenBy(static item => item.Id, StringComparer.Ordinal)
            .ToArray();

    public bool HasPending() => GetPending().Count > 0;

    /// <summary>Counts a pass that could not read the source yet and must be retried later.</summary>
    public void RecordAttempt(string entryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        if (Get(entryId) is not { } item)
        {
            return;
        }

        item.Attempts++;
        Save(item.Id, item);
    }

    public void Remove(string entryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        DatabaseGate.Run(Collection.Database, () =>
        {
            using var document = Collection.GetDocument(entryId);
            if (document is not null)
            {
                Collection.Delete(document);
            }
        });
    }
}
