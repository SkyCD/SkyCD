using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyCD.Presentation.UndoRedo
{
    /// <summary>
    /// Tracks the catalog entries whose media cache artifacts must survive cleanup because undo history can
    /// still bring them back.
    /// </summary>
    /// <remarks>
    /// Deleting a catalog entry orphans its cached previews, but the entry is not really gone while an undo
    /// action holds its snapshot: restoring it restores the original identifier, name and ancestor chain, and
    /// <c>MediaSourceFingerprint</c> derives its key from exactly those. Reclaiming the artifacts before then
    /// would leave the restored entry pointing at previews that no longer exist, which is worse than holding
    /// them a little longer.
    ///
    /// Pins are reference counted rather than a flat set, because two overlapping deletions can hold the same
    /// descendant and only the last release should unpublish it.
    /// </remarks>
    public sealed class CatalogHistoryCachePins
    {
        private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
        private readonly List<PinOwner> _owners = new();
        private readonly object _gate = new();

        /// <summary>Raised whenever a pin count drops to zero, so retention can be re-evaluated.</summary>
        public event EventHandler<CachePinsReleasedEventArgs>? Released;

        public int PinnedEntryCount
        {
            get { lock (_gate) return _counts.Count; }
        }

        public IReadOnlyCollection<string> PinnedEntryIds
        {
            get { lock (_gate) return _counts.Keys.ToArray(); }
        }

        public bool IsPinned(string entryId)
        {
            if (string.IsNullOrWhiteSpace(entryId)) return false;
            lock (_gate) return _counts.ContainsKey(entryId);
        }

        /// <summary>
        /// Records that <paramref name="ownerId"/> needs these entries kept alive. The owner id lets one action
        /// release exactly what it pinned without disturbing a concurrent overlapping deletion.
        /// </summary>
        public void Pin(string ownerId, IEnumerable<string> entryIds)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
            ArgumentNullException.ThrowIfNull(entryIds);

            lock (_gate)
            {
                foreach (var entryId in Distinct(entryIds))
                {
                    _counts.TryGetValue(entryId, out var count);
                    _counts[entryId] = count + 1;
                    _owners.Add(new PinOwner(ownerId, entryId));
                }
            }
        }

        /// <summary>Releases everything <paramref name="ownerId"/> pinned and reports what became unpinned.</summary>
        public void Release(string ownerId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

            List<string>? released = null;
            lock (_gate)
            {
                for (var i = _owners.Count - 1; i >= 0; i--)
                {
                    if (!string.Equals(_owners[i].OwnerId, ownerId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var entryId = _owners[i].EntryId;
                    _owners.RemoveAt(i);

                    if (!_counts.TryGetValue(entryId, out var count))
                    {
                        continue;
                    }

                    if (count <= 1)
                    {
                        _counts.Remove(entryId);
                        (released ??= new List<string>()).Add(entryId);
                    }
                    else
                    {
                        _counts[entryId] = count - 1;
                    }
                }
            }

            if (released is { Count: > 0 })
            {
                Released?.Invoke(this, new CachePinsReleasedEventArgs(released));
            }
        }

        /// <summary>Drops every pin. Used when history is cleared or a different catalog is opened.</summary>
        public void Clear()
        {
            List<string>? released = null;
            lock (_gate)
            {
                if (_counts.Count > 0)
                {
                    released = _counts.Keys.ToList();
                }

                _counts.Clear();
                _owners.Clear();
            }

            if (released is { Count: > 0 })
            {
                Released?.Invoke(this, new CachePinsReleasedEventArgs(released));
            }
        }

        private static IEnumerable<string> Distinct(IEnumerable<string> entryIds) => entryIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal);

        private readonly record struct PinOwner(string OwnerId, string EntryId);
    }
}
