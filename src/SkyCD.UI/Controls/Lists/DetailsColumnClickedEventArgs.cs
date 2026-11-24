using System;

namespace SkyCD.UI.Controls.Lists;

/// <summary>Raised when the user clicks a details header without starting a reorder drag.</summary>
public sealed class DetailsColumnClickedEventArgs : EventArgs
{
    public DetailsColumnClickedEventArgs(string columnId)
    {
        ColumnId = columnId;
    }

    public string ColumnId { get; }
}
