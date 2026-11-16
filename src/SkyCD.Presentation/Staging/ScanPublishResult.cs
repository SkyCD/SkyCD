using System;
using System.Collections.Generic;
using SkyCD.Documents;

namespace SkyCD.Presentation.Staging;

public sealed class ScanPublishResult
{
    public required string GenerationId { get; init; }
    public string? RootDocumentId { get; init; }
    public int PublishedDocumentsCount { get; init; }
    public int InsertedDocumentsCount { get; init; }
    public int UpdatedDocumentsCount { get; init; }
    public bool IsIncomplete { get; init; }
    public string? ErrorSummary { get; init; }
    public IReadOnlyList<CatalogDocument> PublishedDocuments { get; init; } = [];

    /// <summary>
    /// True when the publication was recorded in undo history and can be reversed as one operation.
    /// </summary>
    /// <remarks>
    /// False means the documents are in the catalog but the snapshot was too large for the history to keep,
    /// so Undo would reverse some earlier operation instead. Callers must tell the user this rather than
    /// leaving an enabled Undo command that silently does the wrong thing.
    /// </remarks>
    public bool IsUndoRecorded { get; init; }
}
