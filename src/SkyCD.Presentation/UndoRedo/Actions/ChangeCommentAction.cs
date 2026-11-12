using System.Threading.Tasks;
using System;
using SkyCD.Core.UndoRedo;
using SkyCD.Documents.Repository;
using SkyCD.Documents;
using SkyCD.Documents.Collections;

namespace SkyCD.Presentation.UndoRedo.Actions
{
    public class ChangeCommentAction : UndoableActionBase
    {
        private readonly CatalogDocumentRepository _repository;
        private readonly string _documentId;
        private readonly string? _oldComment;
        private readonly string? _newComment;
        private readonly string _propertyName;

        public ChangeCommentAction(
            CatalogDocumentRepository repository,
            string documentId,
            string? oldComment,
            string? newComment,
            string propertyName = "Comment")
            : base("Change Comment")
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _documentId = documentId ?? throw new ArgumentNullException(nameof(documentId));
            _oldComment = oldComment;
            _newComment = newComment;
            _propertyName = propertyName;
        }

        public override bool CanUndo => true;
        public override bool CanRedo => true;

        public override Task UndoAsync() => ChangeCommentAsync(_newComment, _oldComment);
        public override Task RedoAsync() => ChangeCommentAsync(_oldComment, _newComment);

        private Task ChangeCommentAsync(string? fromComment, string? toComment)
        {
            var doc = _repository.Get(_documentId);
            if (doc is null)
                return Task.CompletedTask;

            var updatedProperties = new PropertiesCollection(doc.Properties);
            if (toComment is not null)
                updatedProperties[_propertyName] = toComment;
            else
                updatedProperties.Remove(_propertyName);

            var updatedDoc = new CatalogDocument
            {
                Id = doc.Id,
                Name = doc.Name,
                ParentId = doc.ParentId,
                Type = doc.Type,
                Size = doc.Size,
                ChildrenCount = doc.ChildrenCount,
                Status = doc.Status,
                Properties = updatedProperties
            };
            _repository.Save(_documentId, updatedDoc);
            return Task.CompletedTask;
        }
    }
}
