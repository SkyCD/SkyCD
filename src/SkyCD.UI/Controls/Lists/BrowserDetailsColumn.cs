using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace SkyCD.UI.Controls.Lists;

public sealed class BrowserDetailsColumn : AvaloniaObject
{
    public static readonly StyledProperty<GridLength> WidthProperty =
        AvaloniaProperty.Register<BrowserDetailsColumn, GridLength>(nameof(Width), new GridLength(1, GridUnitType.Star));

    public required string Header { get; init; }

    public required string ValuePath { get; init; }

    public GridLength Width
    {
        get => GetValue(WidthProperty);
        set => SetValue(WidthProperty, value);
    }

    public HorizontalAlignment HeaderAlignment { get; init; } = HorizontalAlignment.Left;

    public HorizontalAlignment ValueAlignment { get; init; } = HorizontalAlignment.Left;

    public double MinWidth { get; init; } = 30;
}