using System.Threading.Tasks;
using Couchbase.Lite;
using System;
using System.Collections.Generic;
using SkyCD.Core.UndoRedo;
using SkyCD.Documents;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.Catalog;

namespace SkyCD.Presentation.UndoRedo.Actions
{
    /// <summary>
    /// Represents an undoable addition operation on catalog documents.
    /// </summary>
    public class AddCatalogAction : UndoableActionBase
    {
        private readonly CatalogDocumentRepository _repository;
        private readonly List<CatalogDocument> _addedDocuments;
        private readonly IReadOnlyList<string> _externalParentIds;

        public AddCatalogAction(
            CatalogDocumentRepository repository,
            IReadOnlyList<CatalogDocument> addedDocuments)
            : base("Add")
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _addedDocuments = new List<CatalogDocument>(addedDocuments ?? throw new ArgumentNullException(nameof(addedDocuments)));
            _externalParentIds = CatalogSizeMaintenance.ExternalParentIds(_addedDocuments);
        }

        public override bool CanUndo => true;
        public override bool CanRedo => true;

        public override Task UndoAsync()
        {
            // Delete all added documents
            foreach (var doc in _addedDocuments)
            {
                using var document = _repository.Collection.GetDocument(doc.Id);
                if (document is not null)
                {
                    _repository.Collection.Delete(document);
                }
            }
            _repository.InvalidateSnapshot();

            CatalogSizeMaintenance.RecalculateAncestors(_repository, _externalParentIds);
            return Task.CompletedTask;
        }

        public override Task RedoAsync()
        {
            // Re-add all documents
            foreach (var doc in _addedDocuments)
            {
                _repository.Save(doc.Id, doc);
            }

            CatalogSizeMaintenance.RecalculateAncestors(_repository, _externalParentIds);
            return Task.CompletedTask;
        }
    }
}
