using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Core.UndoRedo;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;

namespace SkyCD.Presentation.UndoRedo.Actions
{
    public class ChangeCollectionEntriesAction : UndoableActionBase
    {
        private readonly VirtualCollectionDocumentRepository _collectionRepository;
        private readonly string _collectionId;
        private readonly IReadOnlyList<string> _oldEntryIds;
        private readonly IReadOnlyList<string> _newEntryIds;

        public ChangeCollectionEntriesAction(
            VirtualCollectionDocumentRepository collectionRepository,
            string collectionId,
            IReadOnlyList<string> oldEntryIds,
            IReadOnlyList<string> newEntryIds)
            : base("Change Collection Entries")
        {
            _collectionRepository = collectionRepository ?? throw new ArgumentNullException(nameof(collectionRepository));
            _collectionId = collectionId ?? throw new ArgumentNullException(nameof(collectionId));
            _oldEntryIds = oldEntryIds ?? throw new ArgumentNullException(nameof(oldEntryIds));
            _newEntryIds = newEntryIds ?? throw new ArgumentNullException(nameof(newEntryIds));
        }

        public override bool CanUndo => true;
        public override bool CanRedo => true;

        public override IReadOnlyList<string> AffectedEntryIds => _oldEntryIds
            .Concat(_newEntryIds)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        public override Task UndoAsync()
        {
            Apply(_oldEntryIds);
            return Task.CompletedTask;
        }

        public override Task RedoAsync()
        {
            Apply(_newEntryIds);
            return Task.CompletedTask;
        }

        private void Apply(IReadOnlyList<string> entryIds)
        {
            var collection = _collectionRepository.Get(_collectionId);
            if (collection is null || collection.Kind != Documents.Enum.VirtualCollectionKind.Static)
            {
                return;
            }

            collection.EntryIds = entryIds
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Select(static id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static id => id, StringComparer.Ordinal)
                .ToList();
            _collectionRepository.Save(_collectionId, collection);
        }
    }
}
