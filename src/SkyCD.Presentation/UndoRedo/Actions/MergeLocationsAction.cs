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
    public class MergeLocationsAction : UndoableActionBase
    {
        private readonly CatalogDocumentRepository _catalogRepository;
        private readonly LocationDocumentRepository _locationRepository;
        private readonly string _sourceLocationId;
        private readonly string _sourceLocationName;
        private readonly string _targetLocationId;
        private readonly IReadOnlyDictionary<string, string?> _affectedOldLocationIds;

        public MergeLocationsAction(
            CatalogDocumentRepository catalogRepository,
            LocationDocumentRepository locationRepository,
            string sourceLocationId,
            string sourceLocationName,
            string targetLocationId,
            IReadOnlyDictionary<string, string?> affectedOldLocationIds)
            : base("Merge Locations")
        {
            _catalogRepository = catalogRepository ?? throw new ArgumentNullException(nameof(catalogRepository));
            _locationRepository = locationRepository ?? throw new ArgumentNullException(nameof(locationRepository));
            _sourceLocationId = sourceLocationId ?? throw new ArgumentNullException(nameof(sourceLocationId));
            _sourceLocationName = sourceLocationName ?? throw new ArgumentNullException(nameof(sourceLocationName));
            _targetLocationId = targetLocationId ?? throw new ArgumentNullException(nameof(targetLocationId));
            _affectedOldLocationIds = affectedOldLocationIds ?? throw new ArgumentNullException(nameof(affectedOldLocationIds));
        }

        public override bool CanUndo => true;
        public override bool CanRedo => true;

        public override IReadOnlyList<string> AffectedEntryIds => _affectedOldLocationIds.Keys.ToArray();

        public override Task UndoAsync()
        {
            if (_locationRepository.Get(_sourceLocationId) is null)
            {
                _locationRepository.Save(_sourceLocationId, new LocationDocument { Id = _sourceLocationId, Name = _sourceLocationName });
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
                    string.Equals(oldLocationId, _sourceLocationId, StringComparison.Ordinal)
                        ? _targetLocationId
                        : oldLocationId);
            }

            using var stored = _locationRepository.Collection.GetDocument(_sourceLocationId);
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
