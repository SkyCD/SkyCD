using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Core.UndoRedo;
using SkyCD.Documents;
using SkyCD.Documents.Repository;

namespace SkyCD.Presentation.UndoRedo.Actions
{
    public class DeleteVirtualCollectionAction : UndoableActionBase
    {
        private readonly VirtualCollectionDocumentRepository _collectionRepository;
        private readonly VirtualCollectionDocument _snapshot;

        public DeleteVirtualCollectionAction(
            VirtualCollectionDocumentRepository collectionRepository,
            VirtualCollectionDocument snapshot)
            : base("Delete Collection")
        {
            _collectionRepository = collectionRepository ?? throw new ArgumentNullException(nameof(collectionRepository));
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public override bool CanUndo => true;
        public override bool CanRedo => true;

        public override IReadOnlyList<string> AffectedEntryIds => [];

        public override Task UndoAsync()
        {
            if (_collectionRepository.Get(_snapshot.Id) is null)
            {
                _collectionRepository.Save(_snapshot.Id, Clone(_snapshot));
            }

            return Task.CompletedTask;
        }

        public override Task RedoAsync()
        {
            using var stored = _collectionRepository.Collection.GetDocument(_snapshot.Id);
            if (stored is not null)
            {
                _collectionRepository.Collection.Delete(stored);
            }

            return Task.CompletedTask;
        }

        private static VirtualCollectionDocument Clone(VirtualCollectionDocument source) => new()
        {
            Id = source.Id,
            Name = source.Name,
            Kind = source.Kind,
            EntryIds = source.EntryIds.ToList(),
            SavedQueryJson = source.SavedQueryJson
        };
    }
}
