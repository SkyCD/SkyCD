using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SkyCD.Core.UndoRedo;
using SkyCD.Documents;
using SkyCD.Documents.Repository;

namespace SkyCD.Presentation.UndoRedo.Actions;

/// <summary>
/// Represents an undoable scan publication operation (#434).
/// Allows single-operation rollback or redo of an imported/published scan result.
/// </summary>
public sealed class PublishScanAction : UndoableActionBase
{
    private readonly CatalogDocumentRepository repository;
    private readonly List<CatalogDocument> insertedDocuments;
    private readonly List<(CatalogDocument Previous, CatalogDocument Updated)> updatedDocuments;

    public PublishScanAction(
        CatalogDocumentRepository repository,
        IReadOnlyList<CatalogDocument> insertedDocuments,
        IReadOnlyList<(CatalogDocument Previous, CatalogDocument Updated)>? updatedDocuments = null,
        string? displayName = null)
        : base(displayName ?? DefaultDisplayName(insertedDocuments, updatedDocuments))
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.insertedDocuments = new List<CatalogDocument>(insertedDocuments ?? throw new ArgumentNullException(nameof(insertedDocuments)));
        this.updatedDocuments = new List<(CatalogDocument Previous, CatalogDocument Updated)>(updatedDocuments ?? []);
    }

    public override bool CanUndo => true;
    public override bool CanRedo => true;

    /// <summary>
    /// A completed capture or import is reversed as one operation, so its footprint is the whole snapshot:
    /// every inserted document plus both the before and after state of every merged one. Reporting it lets a
    /// bounded history refuse a publication too large to reverse honestly instead of quietly keeping it.
    /// </summary>
    public override long EstimatedFootprintBytes =>
        UndoFootprint.ForDocuments(insertedDocuments.Count) +
        UndoFootprint.ForDocuments(updatedDocuments.Count * 2);

    public IReadOnlyList<CatalogDocument> InsertedDocuments => insertedDocuments.AsReadOnly();
    public IReadOnlyList<(CatalogDocument Previous, CatalogDocument Updated)> UpdatedDocuments => updatedDocuments.AsReadOnly();

    private static string DefaultDisplayName(
        IReadOnlyList<CatalogDocument>? inserted,
        IReadOnlyList<(CatalogDocument Previous, CatalogDocument Updated)>? updated)
    {
        var insertedCount = inserted?.Count ?? 0;
        var updatedCount = updated?.Count ?? 0;

        if (insertedCount == 0)
        {
            return updatedCount == 0 ? "Publish Scan" : $"Publish Scan ({updatedCount} items)";
        }

        if (insertedCount == 1 && updatedCount == 0)
        {
            return $"Publish Scan ({inserted![0].Name})";
        }

        return $"Publish Scan ({insertedCount + updatedCount} items)";
    }

    public override Task UndoAsync()
    {
        repository.Collection.Database.InBatch(() =>
        {
            // Remove all newly inserted documents
            foreach (var doc in insertedDocuments)
            {
                using var document = repository.Collection.GetDocument(doc.Id);
                if (document is not null)
                {
                    repository.Collection.Delete(document);
                }
            }

            // Restore previous state of updated documents
            foreach (var (previous, _) in updatedDocuments)
            {
                repository.Save(previous.Id, previous);
            }
        });

        return Task.CompletedTask;
    }

    public override Task RedoAsync()
    {
        repository.Collection.Database.InBatch(() =>
        {
            // Re-insert added documents
            foreach (var doc in insertedDocuments)
            {
                repository.Save(doc.Id, doc);
            }

            // Re-apply updated documents
            foreach (var (_, updated) in updatedDocuments)
            {
                repository.Save(updated.Id, updated);
            }
        });

        return Task.CompletedTask;
    }
}
