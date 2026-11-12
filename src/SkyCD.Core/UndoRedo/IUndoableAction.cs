using System;
using System.Threading.Tasks;

namespace SkyCD.Core.UndoRedo
{
    public interface IUndoableAction : IDisposable
    {
        string Id { get; }
        string DisplayName { get; }
        DateTimeOffset Timestamp { get; }
        bool CanUndo { get; }
        bool CanRedo { get; }
        Task UndoAsync();
        Task RedoAsync();
    }

    public abstract class UndoableActionBase : IUndoableAction
    {
        private bool _isDisposed;

        protected UndoableActionBase(string displayName)
        {
            Id = Guid.NewGuid().ToString("N");
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            Timestamp = DateTimeOffset.UtcNow;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public DateTimeOffset Timestamp { get; }
        public abstract bool CanUndo { get; }
        public abstract bool CanRedo { get; }
        public abstract Task UndoAsync();
        public abstract Task RedoAsync();

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed) return;
            _isDisposed = true;
        }
    }
}
