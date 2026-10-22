using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SkyCD.UI.Controls.Lists;
using Xunit;

namespace SkyCD.UI.Tests;

public class BrowserStatusBadgeTests
{
    [AvaloniaTheory]
    [InlineData(BrowserViewMode.Details)]
    [InlineData(BrowserViewMode.List)]
    [InlineData(BrowserViewMode.SmallIcons)]
    [InlineData(BrowserViewMode.LargeIcons)]
    [InlineData(BrowserViewMode.Tiles)]
    public void Templates_ShowBottomRightBadgeAndHideItWhenStatusClears(BrowserViewMode mode)
    {
        var renderedIcon = new Border();
        var view = new BrowserItemsView
        {
            ViewMode = mode,
            StatusIconConverter = new FuncValueConverter<object?, object?>(value =>
                value?.ToString() == "test:icon" ? renderedIcon : null)
        };
        var template = mode == BrowserViewMode.Details
            ? view.FindControl<DetailsListView>("DetailsListView")!.RowTemplate!
            : view.FindControl<ListBox>(mode == BrowserViewMode.List ? "ListModeListBox" : "IconGridListBox")!.ItemTemplate!;
        var item = new BadgeItem();
        var row = template.Build(item)!;
        row.DataContext = item;
        Dispatcher.UIThread.RunJobs();

        var content = row.GetLogicalDescendants().OfType<ContentControl>()
            .Single(control => ReferenceEquals(control.Content, renderedIcon));
        var badge = Assert.IsType<Border>(content.Parent);
        Assert.True(badge.IsVisible);
        Assert.Equal(HorizontalAlignment.Right, badge.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Bottom, badge.VerticalAlignment);
        Assert.Equal("Loaned Out", ToolTip.GetTip(badge));
        Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(content.Foreground).Color);

        row.DataContext = new BadgeItem { Properties = new Dictionary<string, object?>() };
        Dispatcher.UIThread.RunJobs();

        Assert.False(badge.IsVisible);
        Assert.Null(content.Content);
    }

    public sealed class BadgeItem
    {
        public string Name => "Disc";
        public string IconGlyph => "media";
        public string DisplayType => "Media";
        public string DisplaySize => "0";
        public Dictionary<string, object?> Properties { get; init; } = new()
        {
            ["StatusIconGlyph"] = "test:icon",
            ["StatusIconColor"] = "#FF0000",
            ["StatusName"] = "Loaned Out"
        };
    }
}
