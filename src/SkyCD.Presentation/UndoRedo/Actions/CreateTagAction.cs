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
    public class CreateTagAction : UndoableActionBase
    {
        private readonly CatalogDocumentRepository _catalogRepository;
        private readonly TagDocumentRepository _tagRepository;
        private readonly string _tagId;
        private readonly string _tagName;

        public CreateTagAction(
            CatalogDocumentRepository catalogRepository,
            TagDocumentRepository tagRepository,
            string tagId,
            string tagName)
            : base("Create Tag")
        {
            _catalogRepository = catalogRepository ?? throw new ArgumentNullException(nameof(catalogRepository));
            _tagRepository = tagRepository ?? throw new ArgumentNullException(nameof(tagRepository));
            _tagId = tagId ?? throw new ArgumentNullException(nameof(tagId));
            _tagName = tagName ?? throw new ArgumentNullException(nameof(tagName));
        }

        public override bool CanUndo => true;
        public override bool CanRedo => true;

        public override IReadOnlyList<string> AffectedEntryIds => [];

        public override Task UndoAsync()
        {
            StripFromAllEntries();
            var document = _tagRepository.Get(_tagId);
            if (document is not null)
            {
                using var stored = _tagRepository.Collection.GetDocument(_tagId);
                if (stored is not null)
                {
                    _tagRepository.Collection.Delete(stored);
                }
            }

            return Task.CompletedTask;
        }

        public override Task RedoAsync()
        {
            if (_tagRepository.Get(_tagId) is null)
            {
                _tagRepository.Save(_tagId, new TagDocument { Id = _tagId, Name = _tagName });
            }

            return Task.CompletedTask;
        }

        private void StripFromAllEntries()
        {
            foreach (var entry in _catalogRepository.GetAll())
            {
                var assigned = ReadTagIds(entry);
                if (!assigned.Contains(_tagId, StringComparer.Ordinal))
                {
                    continue;
                }

                WriteEntryTags(entry.Id, assigned.Where(id => !string.Equals(id, _tagId, StringComparison.Ordinal)).ToArray());
            }
        }

        private static IReadOnlyList<string> ReadTagIds(CatalogDocument entry)
        {
            if (!entry.Properties.TryGetValue("Tags", out var raw) || raw is null)
            {
                return [];
            }

            return raw is IEnumerable<string> ids
                ? ids.Where(static id => !string.IsNullOrWhiteSpace(id)).ToArray()
                : [];
        }

        private void WriteEntryTags(string entryId, IReadOnlyList<string> tagIds)
        {
            var entry = _catalogRepository.Get(entryId);
            if (entry is null)
            {
                return;
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
                Properties = new PropertiesCollection(entry.Properties)
                {
                    ["Tags"] = tagIds.ToList()
                }
            });
        }
    }
}

