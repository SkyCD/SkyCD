using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace SkyCD.UI.Controls.Lists;

public sealed class BrowserDetailsColumn
{
    public required string Header { get; init; }

    public required string ValuePath { get; init; }

    public GridLength Width { get; init; } = new(1, GridUnitType.Star);

    public HorizontalAlignment HeaderAlignment { get; init; } = HorizontalAlignment.Left;

    public HorizontalAlignment ValueAlignment { get; init; } = HorizontalAlignment.Left;
}
