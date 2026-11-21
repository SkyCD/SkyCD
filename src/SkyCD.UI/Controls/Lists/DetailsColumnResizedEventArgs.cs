using System;

namespace SkyCD.UI.Controls.Lists;

/// <summary>Raised when the user finishes dragging a details header splitter.</summary>
public sealed class DetailsColumnResizedEventArgs : EventArgs
{
    public DetailsColumnResizedEventArgs(string columnId, double width)
    {
        ColumnId = columnId;
        Width = width;
    }

    public string ColumnId { get; }

    /// <summary>Final pixel width of the column.</summary>
    public double Width { get; }
}
