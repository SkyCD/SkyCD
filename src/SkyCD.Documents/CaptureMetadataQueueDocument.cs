using System;
using SkyCD.Couchbase.Attributes;
using SkyCD.Documents.Repository;

namespace SkyCD.Documents;

/// <summary>
/// Durable marker for a captured catalog entry whose metadata extraction has not finished yet. The entry id
/// doubles as the document id, so importing the same capture twice never queues it twice.
/// </summary>
[CouchbaseDocument("capture_metadata_queue", repositoryType: typeof(CaptureMetadataQueueRepository))]
public sealed class CaptureMetadataQueueDocument
{
    [Id] public string Id { get; set; } = string.Empty;

    /// <summary>Catalog entry waiting for metadata extraction; identical to <see cref="Id"/>.</summary>
    public string EntryId { get; set; } = string.Empty;

    public DateTimeOffset EnqueuedAtUtc { get; set; }

    /// <summary>Passes that saw the file unavailable; the item is dropped once the retry budget runs out.</summary>
    public int Attempts { get; set; }
}
