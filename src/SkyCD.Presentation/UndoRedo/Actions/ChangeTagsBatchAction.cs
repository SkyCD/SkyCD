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
    public class ChangeTagsBatchAction : UndoableActionBase
    {
        private readonly CatalogDocumentRepository _repository;
        private readonly IReadOnlyDictionary<string, (IReadOnlyList<string> OldTagIds, IReadOnlyList<string> NewTagIds)> _changes;

        public ChangeTagsBatchAction(
            CatalogDocumentRepository repository,
            IReadOnlyDictionary<string, (IReadOnlyList<string> OldTagIds, IReadOnlyList<string> NewTagIds)> changes)
            : base("Change Tags")
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _changes = changes ?? throw new ArgumentNullException(nameof(changes));
        }

        public override bool CanUndo => true;
        public override bool CanRedo => true;

        public override IReadOnlyList<string> AffectedEntryIds => _changes.Keys.ToArray();

        public override Task UndoAsync()
        {
            return ApplyAsync(static change => change.OldTagIds);
        }

        public override Task RedoAsync()
        {
            return ApplyAsync(static change => change.NewTagIds);
        }

        private Task ApplyAsync(Func<(IReadOnlyList<string> OldTagIds, IReadOnlyList<string> NewTagIds), IReadOnlyList<string>> select)
        {
            foreach (var (documentId, change) in _changes)
            {
                var doc = _repository.Get(documentId);
                if (doc is null)
                    continue;

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
                        ["Tags"] = select(change).ToList()
                    }
                };
                _repository.Save(documentId, updatedDoc);
            }

            return Task.CompletedTask;
        }
    }
}
