using System;

namespace SkyCD.Core.UndoRedo
{
    /// <summary>
    /// Raised when an action is refused because it would exceed the history's footprint budget.
    /// </summary>
    /// <remarks>
    /// The operation itself has normally already been applied to the catalog by the time this is raised; what
    /// is refused is the undo record. Surfacing it is the difference between telling the user "this cannot be
    /// undone" and silently leaving an Undo command that appears to work and then reverses the wrong thing.
    /// </remarks>
    public sealed class UndoActionRejectedEventArgs : EventArgs
    {
        public UndoActionRejectedEventArgs(string displayName, long requestedBytes, long budgetBytes)
        {
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            RequestedBytes = requestedBytes;
            BudgetBytes = budgetBytes;
        }

        public string DisplayName { get; }
        public long RequestedBytes { get; }
        public long BudgetBytes { get; }
    }
}
