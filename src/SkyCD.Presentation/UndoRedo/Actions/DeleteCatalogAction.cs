using System.Threading.Tasks;
using Couchbase.Lite;
using System;
using System.Collections.Generic;
using SkyCD.Core.UndoRedo;
using SkyCD.Documents;
using SkyCD.Documents.Repository;

namespace SkyCD.Presentation.UndoRedo.Actions
{
    /// <summary>
    /// Represents an undoable deletion operation on catalog documents.
    /// Note: Full deletion with hierarchy requires access to Couchbase Collection.Delete.
    /// For now, this stores the deleted documents and restores them on undo.
    /// </summary>
    public class DeleteCatalogAction : UndoableActionBase
    {
        private readonly CatalogDocumentRepository _repository;
        private readonly List<CatalogDocument> _deletedDocuments;

        public DeleteCatalogAction(
            CatalogDocumentRepository repository,
            IReadOnlyList<CatalogDocument> deletedDocuments)
            : base("Delete")
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _deletedDocuments = new List<CatalogDocument>(deletedDocuments ?? throw new ArgumentNullException(nameof(deletedDocuments)));
        }

        public override bool CanUndo => true;
        public override bool CanRedo => true;

        public override Task UndoAsync()
        {
            // Restore all deleted documents
            foreach (var doc in _deletedDocuments)
            {
                _repository.Save(doc.Id, doc);
            }
            return Task.CompletedTask;
        }

        public override Task RedoAsync()
        {
            // Delete all documents using the repository's delete mechanism
            foreach (var doc in _deletedDocuments)
            {
                using var document = _repository.Collection.GetDocument(doc.Id);
                if (document is not null)
                {
                    _repository.Collection.Delete(document);
                }
            }
            return Task.CompletedTask;
        }
    }
}
