using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Core.UndoRedo;
using SkyCD.Documents.Repository;
using SkyCD.Documents;

namespace SkyCD.Presentation.UndoRedo
{
    public class CatalogUndoManager : IDisposable
    {
        private readonly UndoRedoHistory _history;
        private readonly CatalogDocumentRepository _catalogRepository;
        private bool _isDisposed;

        public CatalogUndoManager(CatalogDocumentRepository catalogRepository, int maxHistorySize = 100)
        {
            _catalogRepository = catalogRepository ?? throw new ArgumentNullException(nameof(catalogRepository));
            _history = new UndoRedoHistory(maxHistorySize);
            CachePins = new CatalogHistoryCachePins();
        }

        public UndoRedoHistory History => _history;

        /// <summary>
        /// Entries whose cached previews must survive retention because history can still restore them.
        /// Pass <see cref="CatalogHistoryCachePins.IsPinned"/> into the cache's <c>entryExists</c> predicate so
        /// a deleted-but-undoable entry is not treated as orphaned.
        /// </summary>
        public CatalogHistoryCachePins CachePins { get; }

        /// <summary>Largest single snapshot the history will keep; zero means unlimited.</summary>
        public long MaxActionFootprintBytes
        {
            get => _history.MaxActionFootprintBytes;
            set => _history.MaxActionFootprintBytes = value;
        }

        /// <summary>Forwarded from the history so the UI can report that older operations stopped being reversible.</summary>
        public event EventHandler<UndoActionEvictedEventArgs>? ActionEvicted
        {
            add => _history.ActionEvicted += value;
            remove => _history.ActionEvicted -= value;
        }

        /// <summary>Forwarded from the history so the UI can report an operation that is not undoable.</summary>
        public event EventHandler<UndoActionRejectedEventArgs>? ActionRejected
        {
            add => _history.ActionRejected += value;
            remove => _history.ActionRejected -= value;
        }

        public bool CanUndo => _history.CanUndo;
        public bool CanRedo => _history.CanRedo;
        public string? UndoActionName => _history.UndoActionName;
        public string? RedoActionName => _history.RedoActionName;
        public bool IsDirty => _history.IsDirty;

        public void BeginTransaction(string displayName) => _history.BeginTransaction(displayName);
        public void CommitTransaction() => _history.CommitTransaction();
        public void RollbackTransaction() => _history.RollbackTransaction();
        public Task UndoAsync() => _history.UndoAsync();
        public Task RedoAsync() => _history.RedoAsync();
        public void MarkAsSaved() => _history.MarkAsSaved();
        public void Clear() => _history.Clear();
        public void ResetForNewCatalog() => _history.ResetForNewCatalog();

        public void RecordRename(CatalogDocument document, string oldName, string newName)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (oldName == newName) return;
            _history.PushAction(new Actions.RenameCatalogAction(_catalogRepository, document.Id, oldName, newName));
        }

        public void RecordStatusChange(CatalogDocument document, string? oldStatus, string? newStatus)
        {
            if (document == null) return;
            if (oldStatus == newStatus) return;
            _history.PushAction(new Actions.ChangeStatusAction(_catalogRepository, document.Id, oldStatus, newStatus));
        }

        public void RecordCommentChange(CatalogDocument document, string? oldComment, string? newComment)
        {
            if (document == null) return;
            if (oldComment == newComment) return;
            _history.PushAction(new Actions.ChangeCommentAction(_catalogRepository, document.Id, oldComment, newComment));
        }

        public void RecordPropertyChange(CatalogDocument document, string propertyName, object? oldValue, object? newValue)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (propertyName == null) throw new ArgumentNullException(nameof(propertyName));
            if (Equals(oldValue, newValue)) return;
            _history.PushAction(new Actions.ChangePropertyAction(_catalogRepository, document.Id, propertyName, oldValue, newValue));
        }

        public void RecordTagsChange(CatalogDocument document, IReadOnlyList<string> oldTagIds, IReadOnlyList<string> newTagIds)
        {
            if (document == null) return;
            if (oldTagIds is null || newTagIds is null) return;
            if (oldTagIds.SequenceEqual(newTagIds, StringComparer.Ordinal)) return;
            _history.PushAction(new Actions.ChangeTagsAction(_catalogRepository, document.Id, oldTagIds, newTagIds));
        }

        public void RecordTagsBatchChange(IReadOnlyDictionary<string, (IReadOnlyList<string> OldTagIds, IReadOnlyList<string> NewTagIds)> changes)
        {
            if (changes == null || changes.Count == 0) return;
            _history.PushAction(new Actions.ChangeTagsBatchAction(_catalogRepository, changes));
        }

        /// <summary>
        /// Records a deletion. <paramref name="documents"/> must be the complete removed subtree, not just the
        /// selection, because undo restores exactly what is handed in and anything missing stays deleted.
        /// </summary>
        /// <returns>False when the snapshot was too large for the bounded history and nothing was recorded.</returns>
        public bool RecordDeletion(IReadOnlyList<CatalogDocument> documents)
        {
            if (documents == null || documents.Count == 0) return true;
            return _history.PushAction(new Actions.DeleteCatalogAction(
                _catalogRepository, documents, CachePins));
        }

        public void RecordAddition(IReadOnlyList<CatalogDocument> documents)
        {
            if (documents == null || documents.Count == 0) return;
            _history.PushAction(new Actions.AddCatalogAction(_catalogRepository, documents));
        }

        public void RecordScanPublication(
            IReadOnlyList<CatalogDocument> insertedDocuments,
            IReadOnlyList<(CatalogDocument Previous, CatalogDocument Updated)>? updatedDocuments = null,
            string? displayName = null)
        {
            if ((insertedDocuments == null || insertedDocuments.Count == 0) &&
                (updatedDocuments == null || updatedDocuments.Count == 0))
            {
                return;
            }

            _history.PushAction(new Actions.PublishScanAction(_catalogRepository, insertedDocuments ?? [], updatedDocuments, displayName));
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _history.Dispose();
        }
    }
}
