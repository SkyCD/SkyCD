using System;

namespace SkyCD.UI.Controls.Lists;

/// <summary>Raised when the user drops a details header onto a new position.</summary>
public sealed class DetailsColumnReorderedEventArgs : EventArgs
{
    public DetailsColumnReorderedEventArgs(string columnId, int targetIndex)
    {
        ColumnId = columnId;
        TargetIndex = targetIndex;
    }

    public string ColumnId { get; }

    /// <summary>Target index within the visible column order.</summary>
    public int TargetIndex { get; }
}
