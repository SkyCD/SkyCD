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
    public class DeleteLocationAction : UndoableActionBase
    {
        private readonly CatalogDocumentRepository _catalogRepository;
        private readonly LocationDocumentRepository _locationRepository;
        private readonly string _locationId;
        private readonly string _locationName;
        private readonly IReadOnlyDictionary<string, string?> _affectedOldLocationIds;

        public DeleteLocationAction(
            CatalogDocumentRepository catalogRepository,
            LocationDocumentRepository locationRepository,
            string locationId,
            string locationName,
            IReadOnlyDictionary<string, string?> affectedOldLocationIds)
            : base("Delete Location")
        {
            _catalogRepository = catalogRepository ?? throw new ArgumentNullException(nameof(catalogRepository));
            _locationRepository = locationRepository ?? throw new ArgumentNullException(nameof(locationRepository));
            _locationId = locationId ?? throw new ArgumentNullException(nameof(locationId));
            _locationName = locationName ?? throw new ArgumentNullException(nameof(locationName));
            _affectedOldLocationIds = affectedOldLocationIds ?? throw new ArgumentNullException(nameof(affectedOldLocationIds));
        }

        public override bool CanUndo => true;
        public override bool CanRedo => true;

        public override IReadOnlyList<string> AffectedEntryIds => _affectedOldLocationIds.Keys.ToArray();

        public override Task UndoAsync()
        {
            if (_locationRepository.Get(_locationId) is null)
            {
                _locationRepository.Save(_locationId, new LocationDocument { Id = _locationId, Name = _locationName });
            }

            foreach (var (entryId, oldLocationId) in _affectedOldLocationIds)
            {
                WriteEntryLocationId(entryId, oldLocationId);
            }

            return Task.CompletedTask;
        }

        public override Task RedoAsync()
        {
            foreach (var (entryId, oldLocationId) in _affectedOldLocationIds)
            {
                WriteEntryLocationId(entryId,
                    string.Equals(oldLocationId, _locationId, StringComparison.Ordinal) ? null : oldLocationId);
            }

            using var stored = _locationRepository.Collection.GetDocument(_locationId);
            if (stored is not null)
            {
                _locationRepository.Collection.Delete(stored);
            }

            return Task.CompletedTask;
        }

        private void WriteEntryLocationId(string entryId, string? locationId)
        {
            var entry = _catalogRepository.Get(entryId);
            if (entry is null)
            {
                return;
            }

            var properties = new PropertiesCollection(entry.Properties);
            if (locationId is null)
            {
                properties.Remove("LocationId");
            }
            else
            {
                properties["LocationId"] = locationId;
            }

            _catalogRepository.Save(entryId, new CatalogDocument
            {
                Id = entry.Id,
                Name = entry.Name,
                ParentId = entry.ParentId,
                Type = entry.Type,
                Size = entry.Size,
                ChildrenCount = entry.ChildrenCount,
                ContentsState = entry.ContentsState,
                Status = entry.Status,
                Properties = properties
            });
        }
    }
}
