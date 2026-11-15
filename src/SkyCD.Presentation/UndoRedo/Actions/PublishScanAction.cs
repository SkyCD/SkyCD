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
        : base(displayName ?? "Publish Scan")
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.insertedDocuments = new List<CatalogDocument>(insertedDocuments ?? throw new ArgumentNullException(nameof(insertedDocuments)));
        this.updatedDocuments = new List<(CatalogDocument Previous, CatalogDocument Updated)>(updatedDocuments ?? []);
    }

    public override bool CanUndo => true;
    public override bool CanRedo => true;

    public IReadOnlyList<CatalogDocument> InsertedDocuments => insertedDocuments.AsReadOnly();
    public IReadOnlyList<(CatalogDocument Previous, CatalogDocument Updated)> UpdatedDocuments => updatedDocuments.AsReadOnly();

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
