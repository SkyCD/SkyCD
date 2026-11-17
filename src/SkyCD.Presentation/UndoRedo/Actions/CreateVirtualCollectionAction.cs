using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Core.UndoRedo;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Repository;

namespace SkyCD.Presentation.UndoRedo.Actions
{
    public class CreateVirtualCollectionAction : UndoableActionBase
    {
        private readonly VirtualCollectionDocumentRepository _collectionRepository;
        private readonly string _collectionId;
        private readonly string _name;

        public CreateVirtualCollectionAction(
            VirtualCollectionDocumentRepository collectionRepository,
            string collectionId,
            string name)
            : base("Create Collection")
        {
            _collectionRepository = collectionRepository ?? throw new ArgumentNullException(nameof(collectionRepository));
            _collectionId = collectionId ?? throw new ArgumentNullException(nameof(collectionId));
            _name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public override bool CanUndo => true;
        public override bool CanRedo => true;

        public override IReadOnlyList<string> AffectedEntryIds => [];

        public override Task UndoAsync()
        {
            using var stored = _collectionRepository.Collection.GetDocument(_collectionId);
            if (stored is not null)
            {
                _collectionRepository.Collection.Delete(stored);
            }

            return Task.CompletedTask;
        }

        public override Task RedoAsync()
        {
            if (_collectionRepository.Get(_collectionId) is null)
            {
                _collectionRepository.Save(_collectionId, new VirtualCollectionDocument
                {
                    Id = _collectionId,
                    Name = _name
                });
            }

            return Task.CompletedTask;
        }
    }
}
