using System;

namespace SkyCD.Core.UndoRedo
{
    /// <summary>
    /// Raised when a bounded history drops its oldest action to make room for a new one.
    /// </summary>
    /// <remarks>
    /// The dropped action has already been disposed when this is raised, so anything it pinned — media cache
    /// artifacts, staging references — has been released and can now be reclaimed. Consumers use this to tell
    /// the user that older operations are no longer reversible instead of letting the limit pass silently.
    /// </remarks>
    public sealed class UndoActionEvictedEventArgs : EventArgs
    {
        public UndoActionEvictedEventArgs(string actionId, string displayName, int remainingUndoCount)
        {
            ActionId = actionId ?? throw new ArgumentNullException(nameof(actionId));
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            RemainingUndoCount = remainingUndoCount;
        }

        public string ActionId { get; }
        public string DisplayName { get; }
        public int RemainingUndoCount { get; }
    }
}
