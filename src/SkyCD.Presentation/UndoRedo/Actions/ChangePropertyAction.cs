using System.Threading.Tasks;
using System;
using SkyCD.Core.UndoRedo;
using SkyCD.Documents.Repository;
using SkyCD.Documents;
using SkyCD.Documents.Collections;

namespace SkyCD.Presentation.UndoRedo.Actions
{
    public class ChangePropertyAction : UndoableActionBase
    {
        private readonly CatalogDocumentRepository _repository;
        private readonly string _documentId;
        private readonly string _propertyName;
        private readonly object? _oldValue;
        private readonly object? _newValue;

        public ChangePropertyAction(
            CatalogDocumentRepository repository,
            string documentId,
            string propertyName,
            object? oldValue,
            object? newValue)
            : base("Change Property")
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _documentId = documentId ?? throw new ArgumentNullException(nameof(documentId));
            _propertyName = propertyName ?? throw new ArgumentNullException(nameof(propertyName));
            _oldValue = oldValue;
            _newValue = newValue;
        }

        public override bool CanUndo => true;
        public override bool CanRedo => true;

        public override Task UndoAsync() => ChangePropertyAsync(_newValue, _oldValue);
        public override Task RedoAsync() => ChangePropertyAsync(_oldValue, _newValue);

        private Task ChangePropertyAsync(object? fromValue, object? toValue)
        {
            var doc = _repository.Get(_documentId);
            if (doc is null)
                return Task.CompletedTask;

            var updatedProperties = new PropertiesCollection(doc.Properties);
            if (toValue is not null)
                updatedProperties[_propertyName] = toValue;
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
