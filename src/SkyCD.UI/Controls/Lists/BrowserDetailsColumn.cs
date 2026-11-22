using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace SkyCD.UI.Controls.Lists;

public sealed class BrowserDetailsColumn : AvaloniaObject
{
    public static readonly StyledProperty<GridLength> WidthProperty =
        AvaloniaProperty.Register<BrowserDetailsColumn, GridLength>(nameof(Width), new GridLength(1, GridUnitType.Star));

    /// <summary>Stable catalog identifier used when reordering or resizing the column.</summary>
    public string Id { get; init; } = string.Empty;

    public required string Header { get; init; }

    public required string ValuePath { get; init; }

    /// <summary>
    /// Optional per-item value projection. When set, the row renders the returned text instead of binding
    /// <see cref="ValuePath"/>, so contributed columns can compute values without changing the document model.
    /// </summary>
    public Func<object?, string?>? ValueSelector { get; init; }

    public GridLength Width
    {
        get => GetValue(WidthProperty);
        set => SetValue(WidthProperty, value);
    }

    public HorizontalAlignment HeaderAlignment { get; init; } = HorizontalAlignment.Left;

    public HorizontalAlignment ValueAlignment { get; init; } = HorizontalAlignment.Left;

    public double MinWidth { get; init; } = 30;

    /// <summary>Required columns stay visible and cannot be dragged to a different position.</summary>
    public bool IsRequired { get; init; }
}
