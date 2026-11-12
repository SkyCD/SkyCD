using System.Threading.Tasks;
using System;
using System.Collections.Generic;
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
        }

        public UndoRedoHistory History => _history;
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

        public void RecordDeletion(IReadOnlyList<CatalogDocument> documents)
        {
            if (documents == null || documents.Count == 0) return;
            _history.PushAction(new Actions.DeleteCatalogAction(_catalogRepository, documents));
        }

        public void RecordAddition(IReadOnlyList<CatalogDocument> documents)
        {
            if (documents == null || documents.Count == 0) return;
            _history.PushAction(new Actions.AddCatalogAction(_catalogRepository, documents));
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
