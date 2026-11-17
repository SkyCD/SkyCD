using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Core.UndoRedo;
using SkyCD.Documents.Repository;
using SkyCD.Documents;
using SkyCD.Documents.Collections;

namespace SkyCD.Presentation.UndoRedo.Actions
{
    public class ChangeTagsAction : UndoableActionBase
    {
        private readonly CatalogDocumentRepository _repository;
        private readonly string _documentId;
        private readonly IReadOnlyList<string> _oldTagIds;
        private readonly IReadOnlyList<string> _newTagIds;

        public ChangeTagsAction(
            CatalogDocumentRepository repository,
            string documentId,
            IEnumerable<string> oldTagIds,
            IEnumerable<string> newTagIds)
            : base("Change Tags")
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _documentId = documentId ?? throw new ArgumentNullException(nameof(documentId));
            _oldTagIds = oldTagIds?.ToArray() ?? [];
            _newTagIds = newTagIds?.ToArray() ?? [];
        }

        public override bool CanUndo => true;
        public override bool CanRedo => true;

        public override IReadOnlyList<string> AffectedEntryIds => [_documentId];

        public override Task UndoAsync()
        {
            return ChangeTagsAsync(_newTagIds, _oldTagIds);
        }

        public override Task RedoAsync()
        {
            return ChangeTagsAsync(_oldTagIds, _newTagIds);
        }

        private Task ChangeTagsAsync(IReadOnlyList<string> fromTagIds, IReadOnlyList<string> toTagIds)
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
                ContentsState = doc.ContentsState,
                Status = doc.Status,
                Properties = new PropertiesCollection(doc.Properties)
                {
                    ["Tags"] = toTagIds.ToList()
                }
            };
            _repository.Save(_documentId, updatedDoc);
            return Task.CompletedTask;
        }
    }
}
