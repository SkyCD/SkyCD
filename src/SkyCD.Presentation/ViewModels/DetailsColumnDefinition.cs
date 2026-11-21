using Avalonia.Controls;
using Avalonia.Layout;
using SkyCD.UI.Controls.Lists;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public sealed class DetailsColumnDefinition
{
    public required string Id { get; init; }

    public required string HeaderKey { get; init; }

    public required string ValuePath { get; init; }

    public GridLength Width { get; init; } = new(1, GridUnitType.Star);

    public HorizontalAlignment HeaderAlignment { get; init; } = HorizontalAlignment.Left;

    public HorizontalAlignment ValueAlignment { get; init; } = HorizontalAlignment.Left;

    public bool IsRequired { get; init; }

    public bool IsVisibleByDefault { get; init; } = true;

    public string Header => LocalizationService.Current.Get(HeaderKey);

    /// <summary>
    /// Creates a renderable column. A saved <paramref name="width"/> greater than zero overrides the
    /// catalog default width.
    /// </summary>
    public BrowserDetailsColumn CreateColumn(double? width = null) => new()
    {
        Id = Id,
        Header = Header,
        ValuePath = ValuePath,
        Width = width is > 0 ? new GridLength(width.Value, GridUnitType.Pixel) : Width,
        HeaderAlignment = HeaderAlignment,
        ValueAlignment = ValueAlignment,
        IsRequired = IsRequired
    };
}
