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

    /// <summary>Required columns stay visible; they can still be reordered and resized.</summary>
    public bool IsRequired { get; init; }

    /// <summary>
    /// When set, the row renders the item icon and status indicator inside this column's cell. The icon
    /// follows the column wherever it is moved.
    /// </summary>
    public bool ShowsItemIcon { get; init; }
}
