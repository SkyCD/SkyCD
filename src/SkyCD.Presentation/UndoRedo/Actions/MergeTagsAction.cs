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
    public class MergeTagsAction : UndoableActionBase
    {
        private readonly CatalogDocumentRepository _catalogRepository;
        private readonly TagDocumentRepository _tagRepository;
        private readonly string _sourceTagId;
        private readonly string _sourceTagName;
        private readonly string _targetTagId;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _affectedOldTagIds;

        public MergeTagsAction(
            CatalogDocumentRepository catalogRepository,
            TagDocumentRepository tagRepository,
            string sourceTagId,
            string sourceTagName,
            string targetTagId,
            IReadOnlyDictionary<string, IReadOnlyList<string>> affectedOldTagIds)
            : base("Merge Tags")
        {
            _catalogRepository = catalogRepository ?? throw new ArgumentNullException(nameof(catalogRepository));
            _tagRepository = tagRepository ?? throw new ArgumentNullException(nameof(tagRepository));
            _sourceTagId = sourceTagId ?? throw new ArgumentNullException(nameof(sourceTagId));
            _sourceTagName = sourceTagName ?? throw new ArgumentNullException(nameof(sourceTagName));
            _targetTagId = targetTagId ?? throw new ArgumentNullException(nameof(targetTagId));
            _affectedOldTagIds = affectedOldTagIds ?? throw new ArgumentNullException(nameof(affectedOldTagIds));
        }

        public override bool CanUndo => true;
        public override bool CanRedo => true;

        public override IReadOnlyList<string> AffectedEntryIds => _affectedOldTagIds.Keys.ToArray();

        public override Task UndoAsync()
        {
            if (_tagRepository.Get(_sourceTagId) is null)
            {
                _tagRepository.Save(_sourceTagId, new TagDocument { Id = _sourceTagId, Name = _sourceTagName });
            }

            foreach (var (entryId, oldTagIds) in _affectedOldTagIds)
            {
                WriteEntryTags(entryId, oldTagIds);
            }

            return Task.CompletedTask;
        }

        public override Task RedoAsync()
        {
            foreach (var (entryId, oldTagIds) in _affectedOldTagIds)
            {
                WriteEntryTags(entryId, oldTagIds
                    .Where(id => !string.Equals(id, _sourceTagId, StringComparison.Ordinal))
                    .Append(_targetTagId)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToArray());
            }

            using var stored = _tagRepository.Collection.GetDocument(_sourceTagId);
            if (stored is not null)
            {
                _tagRepository.Collection.Delete(stored);
            }

            return Task.CompletedTask;
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
