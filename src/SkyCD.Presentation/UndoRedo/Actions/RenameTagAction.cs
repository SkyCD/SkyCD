using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using SkyCD.Core.UndoRedo;
using SkyCD.Documents.Repository;
using SkyCD.Documents;

namespace SkyCD.Presentation.UndoRedo.Actions
{
    public class RenameTagAction : UndoableActionBase
    {
        private readonly TagDocumentRepository _tagRepository;
        private readonly string _tagId;
        private readonly string _oldName;
        private readonly string _newName;

        public RenameTagAction(
            TagDocumentRepository tagRepository,
            string tagId,
            string oldName,
            string newName)
            : base("Rename Tag")
        {
            _tagRepository = tagRepository ?? throw new ArgumentNullException(nameof(tagRepository));
            _tagId = tagId ?? throw new ArgumentNullException(nameof(tagId));
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
            var tag = _tagRepository.Get(_tagId);
            if (tag is null)
            {
                return;
            }

            tag.Name = name;
            _tagRepository.Save(_tagId, tag);
        }
    }
}
