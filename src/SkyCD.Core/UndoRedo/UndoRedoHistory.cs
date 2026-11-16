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
        private long _maxActionFootprintBytes;

        public UndoRedoHistory(int maxHistorySize = 100)
        {
            if (maxHistorySize < 1)
                throw new ArgumentOutOfRangeException(nameof(maxHistorySize), "Max history size must be at least 1.");
            _maxHistorySize = maxHistorySize;
        }

        /// <summary>
        /// Largest single action the history will retain, in bytes of estimated footprint. Zero (the default)
        /// means unlimited, which preserves the behaviour of callers that only bound the entry count.
        /// </summary>
        public long MaxActionFootprintBytes
        {
            get { lock (_syncLock) return _maxActionFootprintBytes; }
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value), "The footprint budget cannot be negative.");
                lock (_syncLock) _maxActionFootprintBytes = value;
            }
        }

        /// <summary>Raised after the oldest action is dropped to stay within <see cref="_maxHistorySize"/>.</summary>
        public event EventHandler<UndoActionEvictedEventArgs>? ActionEvicted;

        /// <summary>Raised when an action is refused for exceeding <see cref="MaxActionFootprintBytes"/>.</summary>
        public event EventHandler<UndoActionRejectedEventArgs>? ActionRejected;

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;
        public string? UndoActionName => _undoStack.Count > 0 ? _undoStack.Last?.Value.DisplayName : null;
        public string? RedoActionName => _redoStack.Count > 0 ? _redoStack.Last?.Value.DisplayName : null;
        public int SavedRevisionIndex => _savedRevisionIndex;
        public bool IsDirty => _savedRevisionIndex != _undoStack.Count;
        public int UndoCount => _undoStack.Count;
        public int RedoCount => _redoStack.Count;
        public bool IsTransactionActive => _isTransactionActive;

        /// <summary>Combined estimated footprint of everything currently retained.</summary>
        public long TotalFootprintBytes
        {
            get
            {
                lock (_syncLock)
                {
                    return _undoStack.Sum(action => action.EstimatedFootprintBytes)
                           + _redoStack.Sum(action => action.EstimatedFootprintBytes);
                }
            }
        }

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

        /// <summary>
        /// Records an action, returning whether it was accepted.
        /// </summary>
        /// <returns>
        /// False when the action was refused for exceeding <see cref="MaxActionFootprintBytes"/>, in which case
        /// it is disposed and nothing is recorded. A caller that has already applied the operation to the
        /// catalog must treat a false result as "this is not undoable" and say so, rather than leaving an Undo
        /// command that reverses some earlier operation instead.
        /// </returns>
        public bool PushAction(IUndoableAction action)
        {
            ArgumentNullException.ThrowIfNull(action);

            UndoActionRejectedEventArgs? rejection = null;
            List<UndoActionEvictedEventArgs>? evictions = null;

            lock (_syncLock)
            {
                if (_isTransactionActive && _currentTransaction is not null)
                {
                    _currentTransaction.AddAction(action);
                    return true;
                }

                var footprint = action.EstimatedFootprintBytes;
                if (_maxActionFootprintBytes > 0 && footprint > _maxActionFootprintBytes)
                {
                    rejection = new UndoActionRejectedEventArgs(action.DisplayName, footprint, _maxActionFootprintBytes);
                    action.Dispose();
                }
                else
                {
                    evictions = PushActionInternal(action);
                }
            }

            // Raised outside the lock: a handler that inspects the history would otherwise deadlock.
            if (evictions is not null)
            {
                foreach (var eviction in evictions)
                {
                    ActionEvicted?.Invoke(this, eviction);
                }
            }

            if (rejection is not null)
            {
                ActionRejected?.Invoke(this, rejection);
                return false;
            }

            return true;
        }

        private List<UndoActionEvictedEventArgs>? PushActionInternal(IUndoableAction action)
        {
            ClearRedoStack();
            _undoStack.AddLast(action);

            List<UndoActionEvictedEventArgs>? evictions = null;
            while (_undoStack.Count > _maxHistorySize)
            {
                var oldest = _undoStack.First;
                if (oldest is null)
                {
                    break;
                }

                var evicted = oldest.Value;
                _undoStack.RemoveFirst();

                // Disposing releases anything the action pinned, which is what lets a later retention pass
                // reclaim the preview artifacts it was holding on behalf of history.
                evicted.Dispose();

                if (_savedRevisionIndex > 0)
                    _savedRevisionIndex--;

                (evictions ??= new List<UndoActionEvictedEventArgs>()).Add(
                    new UndoActionEvictedEventArgs(evicted.Id, evicted.DisplayName, _undoStack.Count));
            }

            return evictions;
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
            try
            {
                await action.UndoAsync();
            }
            catch
            {
                lock (_syncLock)
                {
                    _redoStack.Remove(action);
                    _undoStack.AddLast(action);
                }
                throw;
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
            try
            {
                await action.RedoAsync();
            }
            catch
            {
                lock (_syncLock)
                {
                    _undoStack.Remove(action);
                    _redoStack.AddLast(action);
                }
                throw;
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
                ClearUndoStack();
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

        /// <summary>A transaction retains everything its children retain.</summary>
        public long EstimatedFootprintBytes => _actions.Sum(action => action.EstimatedFootprintBytes);

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
