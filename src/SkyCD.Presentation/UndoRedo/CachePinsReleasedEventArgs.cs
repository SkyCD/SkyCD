using System;
using System.Collections.Generic;

namespace SkyCD.Presentation.UndoRedo
{
    /// <summary>The entries that stopped being pinned, and can now be reclaimed if still orphaned.</summary>
    public sealed class CachePinsReleasedEventArgs : EventArgs
    {
        public CachePinsReleasedEventArgs(IReadOnlyCollection<string> entryIds)
        {
            EntryIds = entryIds ?? throw new ArgumentNullException(nameof(entryIds));
        }

        public IReadOnlyCollection<string> EntryIds { get; }
    }
}
