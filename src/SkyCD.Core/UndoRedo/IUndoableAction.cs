using System;
using System.Linq;
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

        /// <summary>
        /// Approximate bytes this action retains for as long as it stays in history.
        /// </summary>
        /// <remarks>
        /// A bounded history needs to bound what it holds, not just how many entries it keeps: one action
        /// that snapshots a large subtree can outweigh a hundred small ones. The history compares this
        /// against its own budget and refuses an action that cannot fit, so an operation is never recorded as
        /// undoable when reversing it would require memory the application has not agreed to hold. Zero means
        /// the action keeps nothing beyond its own object.
        /// </remarks>
        long EstimatedFootprintBytes => 0;

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

        /// <inheritdoc/>
        public virtual long EstimatedFootprintBytes => 0;

        /// <summary>True once <see cref="Dispose()"/> has run, so subclasses can skip release work twice.</summary>
        protected bool IsDisposed => _isDisposed;

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

    /// <summary>Helpers for estimating how much an action retains while it sits in history.</summary>
    public static class UndoFootprint
    {
        /// <summary>
        /// Per-document allowance used when an action estimates its footprint. A catalog entry is a handful
        /// of short strings plus a property dictionary, so this is an order-of-magnitude figure: the budget
        /// exists to stop a million-entry import being recorded as undoable, not to account for every byte.
        /// </summary>
        public const long BytesPerDocument = 2048;

        public static long ForDocuments(int documentCount) =>
            (long)Math.Max(0, documentCount) * BytesPerDocument;

        public static long ForDocuments<T>(System.Collections.Generic.IEnumerable<T>? documents) =>
            documents is null ? 0 : ForDocuments(documents.Count());
    }
}
