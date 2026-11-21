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

    /// <summary>
    /// Raw localized header including access-key markers; keep it for menu surfaces only.
    /// </summary>
    public string Header => LocalizationService.Current.Get(HeaderKey);

    /// <summary>
    /// Header without access-key markers for plain-text surfaces such as column headers.
    /// </summary>
    public string DisplayHeader => MnemonicText.Strip(Header);

    public BrowserDetailsColumn CreateColumn() => new()
    {
        Header = DisplayHeader,
        ValuePath = ValuePath,
        Width = Width,
        HeaderAlignment = HeaderAlignment,
        ValueAlignment = ValueAlignment
    };
}
