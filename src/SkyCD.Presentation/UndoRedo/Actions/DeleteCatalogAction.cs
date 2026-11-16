using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SkyCD.Core.UndoRedo;
using SkyCD.Documents;
using SkyCD.Documents.Repository;

namespace SkyCD.Presentation.UndoRedo.Actions
{
    /// <summary>
    /// An undoable catalog deletion that carries the whole deleted subtree.
    /// </summary>
    /// <remarks>
    /// The snapshot is the complete set of documents the deletion removed — the selected entries *and* every
    /// descendant — captured from the repository before anything was deleted, so it carries the authoritative
    /// ids, names, parent links, properties, tags and status. Restoring that set is what makes the media cache
    /// fingerprints of the subtree resolve again, because those are derived from the ids and the ancestor name
    /// chain rather than from anything the UI still holds.
    ///
    /// Redo replays the captured ids instead of re-reading the current selection, so deleting something else
    /// in between cannot redirect it. Undo and redo each run inside a single Couchbase batch, so a failure
    /// part-way through does not leave half a subtree restored; the history puts the action back on the stack
    /// it came from and the caller reports the failure.
    /// </remarks>
    public class DeleteCatalogAction : UndoableActionBase
    {
        private readonly CatalogDocumentRepository _repository;
        private readonly List<CatalogDocument> _deletedDocuments;
        private readonly List<CatalogDocument> _restoreOrder;
        private readonly List<CatalogDocument> _removalOrder;
        private readonly CatalogHistoryCachePins? _pins;

        public DeleteCatalogAction(
            CatalogDocumentRepository repository,
            IReadOnlyList<CatalogDocument> deletedDocuments,
            CatalogHistoryCachePins? pins = null,
            string? displayName = null)
            : base(displayName ?? DefaultDisplayName(deletedDocuments))
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _deletedDocuments = new List<CatalogDocument>(
                deletedDocuments ?? throw new ArgumentNullException(nameof(deletedDocuments)));
            _pins = pins;

            _restoreOrder = OrderParentsFirst(_deletedDocuments);
            _removalOrder = _restoreOrder.AsEnumerable().Reverse().ToList();

            // Artifacts of a deleted subtree stay reclaimable-blocked only for as long as this action can
            // still restore it, which ends when history evicts or clears it and disposes it here.
            _pins?.Pin(Id, _deletedDocuments.Select(static document => document.Id));
        }

        /// <summary>The captured subtree, parents first. Exposed for diagnostics and tests.</summary>
        public IReadOnlyList<CatalogDocument> DeletedDocuments => _restoreOrder;

        public override bool CanUndo => true;
        public override bool CanRedo => true;

        public override long EstimatedFootprintBytes => UndoFootprint.ForDocuments(_deletedDocuments.Count);

        public override Task UndoAsync()
        {
            if (_restoreOrder.Count == 0)
            {
                return Task.CompletedTask;
            }

            _repository.Collection.Database.InBatch(() =>
            {
                foreach (var document in _restoreOrder)
                {
                    _repository.Save(document.Id, document);
                }
            });

            return Task.CompletedTask;
        }

        public override Task RedoAsync()
        {
            if (_removalOrder.Count == 0)
            {
                return Task.CompletedTask;
            }

            _repository.Collection.Database.InBatch(() =>
            {
                foreach (var document in _removalOrder)
                {
                    DeleteFromCollection(document.Id);
                }
            });

            return Task.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            if (IsDisposed)
            {
                return;
            }

            if (disposing)
            {
                _pins?.Release(Id);
            }

            base.Dispose(disposing);
        }

        private void DeleteFromCollection(string id)
        {
            using var document = _repository.Collection.GetDocument(id);
            if (document is not null)
            {
                _repository.Collection.Delete(document);
            }
        }

        private static string DefaultDisplayName(IReadOnlyList<CatalogDocument>? documents)
        {
            var count = documents?.Count ?? 0;
            return count switch
            {
                0 => "Delete",
                1 => $"Delete {documents![0].Name}",
                _ => $"Delete {count} items"
            };
        }

        /// <summary>
        /// Orders a snapshot so parents precede descendants. Couchbase does not require it — <c>ParentId</c> is
        /// an ordinary field — but restoring in this order means the tree is consistent after every step, and
        /// reversing the result gives children-before-parents for removal.
        /// </summary>
        private static List<CatalogDocument> OrderParentsFirst(IReadOnlyList<CatalogDocument> documents)
        {
            var byId = new Dictionary<string, CatalogDocument>(StringComparer.Ordinal);
            foreach (var document in documents)
            {
                if (!string.IsNullOrWhiteSpace(document.Id))
                {
                    byId.TryAdd(document.Id, document);
                }
            }

            var ordered = new List<CatalogDocument>(documents.Count);
            var placed = new HashSet<string>(StringComparer.Ordinal);
            var remaining = byId.Values.ToList();

            // A parent outside the snapshot is treated as already present, which keeps the pass terminating
            // even if the snapshot is somehow missing an intermediate level.
            while (remaining.Count > 0)
            {
                var progressed = false;
                for (var i = remaining.Count - 1; i >= 0; i--)
                {
                    var document = remaining[i];
                    var parentId = document.ParentId;
                    var parentIsReady = string.IsNullOrWhiteSpace(parentId)
                                        || !byId.ContainsKey(parentId)
                                        || placed.Contains(parentId);
                    if (!parentIsReady)
                    {
                        continue;
                    }

                    ordered.Add(document);
                    placed.Add(document.Id);
                    remaining.RemoveAt(i);
                    progressed = true;
                }

                if (!progressed)
                {
                    // A cycle in ParentId cannot be ordered; append what is left so nothing is silently lost.
                    ordered.AddRange(remaining);
                    break;
                }
            }

            return ordered;
        }
    }
}
