using System.Threading.Tasks;
using System;
using SkyCD.Core.UndoRedo;
using SkyCD.Documents.Repository;
using SkyCD.Documents;
using SkyCD.Documents.Collections;

namespace SkyCD.Presentation.UndoRedo.Actions
{
    public class RenameCatalogAction : UndoableActionBase
    {
        private readonly CatalogDocumentRepository _repository;
        private readonly string _documentId;
        private readonly string _oldName;
        private readonly string _newName;

        public RenameCatalogAction(
            CatalogDocumentRepository repository,
            string documentId,
            string oldName,
            string newName)
            : base("Rename")
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _documentId = documentId ?? throw new ArgumentNullException(nameof(documentId));
            _oldName = oldName ?? throw new ArgumentNullException(nameof(oldName));
            _newName = newName ?? throw new ArgumentNullException(nameof(newName));
        }

        public override bool CanUndo => true;
        public override bool CanRedo => true;

        public override Task UndoAsync()
        {
            return RenameAsync(_newName, _oldName);
        }

        public override Task RedoAsync()
        {
            return RenameAsync(_oldName, _newName);
        }

        private Task RenameAsync(string fromName, string toName)
        {
            var doc = _repository.Get(_documentId);
            if (doc is null)
                return Task.CompletedTask;

            var updatedDoc = new CatalogDocument
            {
                Id = doc.Id,
                Name = toName,
                ParentId = doc.ParentId,
                Type = doc.Type,
                Size = doc.Size,
                ChildrenCount = doc.ChildrenCount,
                ContentsState = doc.ContentsState,
                Status = doc.Status,
                Properties = new PropertiesCollection(doc.Properties)
            };
            _repository.Save(_documentId, updatedDoc);
            return Task.CompletedTask;
        }
    }
}
