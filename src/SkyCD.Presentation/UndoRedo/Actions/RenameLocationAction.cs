using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using SkyCD.Core.UndoRedo;
using SkyCD.Documents.Repository;
using SkyCD.Documents;

namespace SkyCD.Presentation.UndoRedo.Actions
{
    public class RenameLocationAction : UndoableActionBase
    {
        private readonly LocationDocumentRepository _locationRepository;
        private readonly string _locationId;
        private readonly string _oldName;
        private readonly string _newName;

        public RenameLocationAction(
            LocationDocumentRepository locationRepository,
            string locationId,
            string oldName,
            string newName)
            : base("Rename Location")
        {
            _locationRepository = locationRepository ?? throw new ArgumentNullException(nameof(locationRepository));
            _locationId = locationId ?? throw new ArgumentNullException(nameof(locationId));
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
            var location = _locationRepository.Get(_locationId);
            if (location is null)
            {
                return;
            }

            location.Name = name;
            _locationRepository.Save(_locationId, location);
        }
    }
}
