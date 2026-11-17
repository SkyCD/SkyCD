using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using SkyCD.Core.UndoRedo;
using SkyCD.Documents.Repository;

namespace SkyCD.Presentation.UndoRedo.Actions
{
    public class RenameVirtualCollectionAction : UndoableActionBase
    {
        private readonly VirtualCollectionDocumentRepository _collectionRepository;
        private readonly string _collectionId;
        private readonly string _oldName;
        private readonly string _newName;

        public RenameVirtualCollectionAction(
            VirtualCollectionDocumentRepository collectionRepository,
            string collectionId,
            string oldName,
            string newName)
            : base("Rename Collection")
        {
            _collectionRepository = collectionRepository ?? throw new ArgumentNullException(nameof(collectionRepository));
            _collectionId = collectionId ?? throw new ArgumentNullException(nameof(collectionId));
            _oldName = oldName ?? throw new ArgumentNullException(nameof(oldName));
            _newName = newName ?? throw new ArgumentNullException(nameof(newName));
        }

        public override bool CanUndo => true;
        public override bool CanRedo => true;

        public override IReadOnlyList<string> AffectedEntryIds => [];

        public override Task UndoAsync()
        {
            ApplyName(_oldName);
            return Task.CompletedTask;
        }

        public override Task RedoAsync()
        {
            ApplyName(_newName);
            return Task.CompletedTask;
        }

        private void ApplyName(string name)
        {
            var collection = _collectionRepository.Get(_collectionId);
            if (collection is null)
            {
                return;
            }

            collection.Name = name;
            _collectionRepository.Save(_collectionId, collection);
        }
    }
}
