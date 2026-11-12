using System.Threading.Tasks;
using System;
using SkyCD.Core.UndoRedo;
using SkyCD.Documents.Repository;
using SkyCD.Documents;
using SkyCD.Documents.Collections;

namespace SkyCD.Presentation.UndoRedo.Actions
{
    public class ChangeStatusAction : UndoableActionBase
    {
        private readonly CatalogDocumentRepository _repository;
        private readonly string _documentId;
        private readonly string? _oldStatus;
        private readonly string? _newStatus;

        public ChangeStatusAction(
            CatalogDocumentRepository repository,
            string documentId,
            string? oldStatus,
            string? newStatus)
            : base("Change Status")
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _documentId = documentId ?? throw new ArgumentNullException(nameof(documentId));
            _oldStatus = oldStatus;
            _newStatus = newStatus;
        }

        public override bool CanUndo => true;
        public override bool CanRedo => true;

        public override Task UndoAsync()
        {
            return ChangeStatusAsync(_newStatus, _oldStatus);
        }

        public override Task RedoAsync()
        {
            return ChangeStatusAsync(_oldStatus, _newStatus);
        }

        private Task ChangeStatusAsync(string? fromStatus, string? toStatus)
        {
            var doc = _repository.Get(_documentId);
            if (doc is null)
                return Task.CompletedTask;

            var updatedDoc = new CatalogDocument
            {
                Id = doc.Id,
                Name = doc.Name,
                ParentId = doc.ParentId,
                Type = doc.Type,
                Size = doc.Size,
                ChildrenCount = doc.ChildrenCount,
                Status = toStatus,
                Properties = new PropertiesCollection(doc.Properties)
            };
            _repository.Save(_documentId, updatedDoc);
            return Task.CompletedTask;
        }
    }
}
