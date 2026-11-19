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
    public class CreateLocationAction : UndoableActionBase
    {
        private readonly CatalogDocumentRepository _catalogRepository;
        private readonly LocationDocumentRepository _locationRepository;
        private readonly string _locationId;
        private readonly string _locationName;

        public CreateLocationAction(
            CatalogDocumentRepository catalogRepository,
            LocationDocumentRepository locationRepository,
            string locationId,
            string locationName)
            : base("Create Location")
        {
            _catalogRepository = catalogRepository ?? throw new ArgumentNullException(nameof(catalogRepository));
            _locationRepository = locationRepository ?? throw new ArgumentNullException(nameof(locationRepository));
            _locationId = locationId ?? throw new ArgumentNullException(nameof(locationId));
            _locationName = locationName ?? throw new ArgumentNullException(nameof(locationName));
        }

        public override bool CanUndo => true;
        public override bool CanRedo => true;

        public override IReadOnlyList<string> AffectedEntryIds => [];

        public override Task UndoAsync()
        {
            StripFromAllEntries();
            if (_locationRepository.Get(_locationId) is not null)
            {
                using var stored = _locationRepository.Collection.GetDocument(_locationId);
                if (stored is not null)
                {
                    _locationRepository.Collection.Delete(stored);
                }
            }

            return Task.CompletedTask;
        }

        public override Task RedoAsync()
        {
            if (_locationRepository.Get(_locationId) is null)
            {
                _locationRepository.Save(_locationId, new LocationDocument { Id = _locationId, Name = _locationName });
            }

            return Task.CompletedTask;
        }

        private void StripFromAllEntries()
        {
            foreach (var entry in _catalogRepository.GetAll())
            {
                if (!ReadOwnLocationId(entry, out var assigned) ||
                    !string.Equals(assigned, _locationId, StringComparison.Ordinal))
                {
                    continue;
                }

                WriteEntryLocationId(entry.Id, null);
            }
        }

        private static bool ReadOwnLocationId(CatalogDocument entry, out string locationId)
        {
            locationId = string.Empty;
            if (!entry.Properties.TryGetValue("LocationId", out var raw) || raw is null)
            {
                return false;
            }

            var text = Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture)?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            locationId = text!;
            return true;
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
