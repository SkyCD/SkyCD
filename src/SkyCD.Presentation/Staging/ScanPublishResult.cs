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
}
