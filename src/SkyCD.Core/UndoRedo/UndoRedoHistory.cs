using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SkyCD.Core.UndoRedo
{
    public class UndoRedoHistory : IDisposable
    {
        private readonly LinkedList<IUndoableAction> _undoStack = new();
        private readonly LinkedList<IUndoableAction> _redoStack = new();
        private readonly object _syncLock = new();
        private readonly int _maxHistorySize;
        private int _savedRevisionIndex = -1;
        private bool _isDisposed;
        private bool _isTransactionActive;
        private UndoTransaction? _currentTransaction;

        public UndoRedoHistory(int maxHistorySize = 100)
        {
            if (maxHistorySize < 1)
                throw new ArgumentOutOfRangeException(nameof(maxHistorySize), "Max history size must be at least 1.");
            _maxHistorySize = maxHistorySize;
        }

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;
        public string? UndoActionName => _undoStack.Count > 0 ? _undoStack.Last?.Value.DisplayName : null;
        public string? RedoActionName => _redoStack.Count > 0 ? _redoStack.Last?.Value.DisplayName : null;
        public int SavedRevisionIndex => _savedRevisionIndex;
        public bool IsDirty => _savedRevisionIndex != _undoStack.Count;
        public int UndoCount => _undoStack.Count;
        public int RedoCount => _redoStack.Count;
        public bool IsTransactionActive => _isTransactionActive;

        public void BeginTransaction(string displayName)
        {
            if (_isTransactionActive)
                throw new InvalidOperationException("A transaction is already active.");
            lock (_syncLock)
            {
                if (_isTransactionActive)
                    throw new InvalidOperationException("A transaction is already active.");
                _isTransactionActive = true;
                _currentTransaction = new UndoTransaction(displayName);
            }
        }

        public void CommitTransaction()
        {
            if (!_isTransactionActive)
                throw new InvalidOperationException("No active transaction to commit.");
            lock (_syncLock)
            {
                if (!_isTransactionActive || _currentTransaction is null)
                    return;
                if (_currentTransaction.Actions.Count > 0)
                {
                    PushActionInternal(_currentTransaction);
                }
                else
                {
                    _currentTransaction.Dispose();
                }
                _currentTransaction = null;
                _isTransactionActive = false;
            }
        }

        public void RollbackTransaction()
        {
            if (!_isTransactionActive)
                throw new InvalidOperationException("No active transaction to rollback.");
            lock (_syncLock)
            {
                _currentTransaction?.Dispose();
                _currentTransaction = null;
                _isTransactionActive = false;
            }
        }

        public void PushAction(IUndoableAction action)
        {
            ArgumentNullException.ThrowIfNull(action);
            lock (_syncLock)
            {
                if (_isTransactionActive && _currentTransaction is not null)
                {
                    _currentTransaction.AddAction(action);
                    return;
                }
                PushActionInternal(action);
            }
        }

        private void PushActionInternal(IUndoableAction action)
        {
            ClearRedoStack();
            _undoStack.AddLast(action);
            while (_undoStack.Count > _maxHistorySize)
            {
                var oldest = _undoStack.First;
                if (oldest is not null)
                {
                    oldest.Value.Dispose();
                    _undoStack.RemoveFirst();
                }
            }
        }

        public async Task UndoAsync()
        {
            IUndoableAction? action = null;
            lock (_syncLock)
            {
                if (_undoStack.Count == 0)
                    return;
                action = _undoStack.Last!.Value;
                _undoStack.RemoveLast();
                _redoStack.AddLast(action);
            }
            if (action is not null)
            {
                await action.UndoAsync();
            }
        }

        public async Task RedoAsync()
        {
            IUndoableAction? action = null;
            lock (_syncLock)
            {
                if (_redoStack.Count == 0)
                    return;
                action = _redoStack.Last!.Value;
                _redoStack.RemoveLast();
                _undoStack.AddLast(action);
            }
            if (action is not null)
            {
                await action.RedoAsync();
            }
        }

        public void MarkAsSaved()
        {
            lock (_syncLock)
            {
                _savedRevisionIndex = _undoStack.Count;
            }
        }

        public void Clear()
        {
            lock (_syncLock)
            {
                ClearUndoStack();
                ClearRedoStack();
                _savedRevisionIndex = -1;
            }
        }

        public void ResetForNewCatalog()
        {
            lock (_syncLock)
            {
                ClearRedoStack();
                _savedRevisionIndex = -1;
            }
        }

        private void ClearUndoStack()
        {
            foreach (var action in _undoStack)
            {
                action.Dispose();
            }
            _undoStack.Clear();
        }

        private void ClearRedoStack()
        {
            foreach (var action in _redoStack)
            {
                action.Dispose();
            }
            _redoStack.Clear();
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
            lock (_syncLock)
            {
                ClearUndoStack();
                ClearRedoStack();
                _currentTransaction?.Dispose();
                _currentTransaction = null;
            }
        }
    }

    public class UndoTransaction : IUndoableAction
    {
        private readonly List<IUndoableAction> _actions = new();
        private bool _isDisposed;

        public UndoTransaction(string displayName)
        {
            Id = Guid.NewGuid().ToString("N");
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            Timestamp = DateTimeOffset.UtcNow;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public DateTimeOffset Timestamp { get; }
        public bool CanUndo => _actions.Count > 0 && _actions.TrueForAll(a => a.CanUndo);
        public bool CanRedo => _actions.Count > 0 && _actions.TrueForAll(a => a.CanRedo);
        public IReadOnlyList<IUndoableAction> Actions => _actions.AsReadOnly();

        public void AddAction(IUndoableAction action)
        {
            ArgumentNullException.ThrowIfNull(action);
            _actions.Add(action);
        }

        public async Task UndoAsync()
        {
            for (int i = _actions.Count - 1; i >= 0; i--)
            {
                await _actions[i].UndoAsync();
            }
        }

        public async Task RedoAsync()
        {
            foreach (var action in _actions)
            {
                await action.RedoAsync();
            }
        }

        public void Clear()
        {
            foreach (var action in _actions)
            {
                action.Dispose();
            }
            _actions.Clear();
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
            Clear();
        }
    }
}
