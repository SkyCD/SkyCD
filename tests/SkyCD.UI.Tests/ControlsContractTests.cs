using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Layout;
using SkyCD.UI.Controls.Lists;
using SkyCD.UI.Controls.Properties;
using SkyCD.UI.Controls.StatusBar;
using SkyCD.UI.Controls.Toolbars;
using Xunit;

namespace SkyCD.UI.Tests;

public class ControlsContractTests
{
    [Fact]
    public void ClassicToolbar_Items_AcceptsToolbarInterfaceTypes()
    {
        var toolbar = new ClassicToolbar();

        toolbar.Items.Add(new ClassicToolbarButton());
        toolbar.Items.Add(new ClassicToolbarSeparator());

        Assert.Equal(2, toolbar.Items.Count);
        Assert.All(toolbar.Items, item => Assert.IsAssignableFrom<IClassicToolbarItem>(item));
    }

    [Fact]
    public void PropertiesList_ProjectsDictionaryIntoRows()
    {
        var list = new PropertiesList();
        list.PropertiesData = new Dictionary<string, object?>
        {
            ["Type"] = "Audio",
            ["Size"] = 414,
            ["Location"] = "Music"
        };

        Assert.Equal(3, list.PropertiesRows.Count);
        Assert.Contains(list.PropertiesRows, row => row.Key == "Type" && row.Value == "Audio");
        Assert.Contains(list.PropertiesRows, row => row.Key == "Size" && row.Value == "414");
        Assert.Contains(list.PropertiesRows, row => row.Key == "Location" && row.Value == "Music");
    }

    [Fact]
    public void DetailsListView_ExposesGenericBindableProperties()
    {
        var view = new DetailsListView();
        var source = new[] { "a", "b" };
        var selected = "b";
        var contextMenu = new ContextMenu();

        view.ItemsSource = source;
        view.SelectedItem = selected;
        view.ListMinWidth = 240;
        view.ListContextMenu = contextMenu;

        Assert.Equal(source, view.ItemsSource);
        Assert.Equal(selected, view.SelectedItem);
        Assert.Equal(240, view.ListMinWidth);
        Assert.Same(contextMenu, view.ListContextMenu);
    }

    [Fact]
    public void BrowserItemsView_ExposesBindablePropertiesAndViewModeFlags()
    {
        var view = new BrowserItemsView();
        var source = new[] { "one", "two" };
        var selected = "two";
        var contextMenu = new ContextMenu();
        var columns = new[]
        {
            new BrowserDetailsColumn
            {
                Header = "Name",
                ValuePath = "Name",
                Width = new GridLength(100, GridUnitType.Pixel),
                HeaderAlignment = HorizontalAlignment.Center,
                ValueAlignment = HorizontalAlignment.Right
            }
        };

        view.ItemsSource = source;
        view.SelectedItem = selected;
        view.ListContextMenu = contextMenu;
        view.ListMinWidth = 280;
        view.BrowserGridItemWidth = 320;
        view.BrowserGridItemHeight = 90;
        view.DetailsColumns = columns;
        view.ViewMode = BrowserViewMode.Tiles;

        Assert.Equal(source, view.ItemsSource);
        Assert.Equal(selected, view.SelectedItem);
        Assert.Same(contextMenu, view.ListContextMenu);
        Assert.Equal(280, view.ListMinWidth);
        Assert.Equal(320, view.BrowserGridItemWidth);
        Assert.Equal(90, view.BrowserGridItemHeight);
        Assert.Same(columns, view.DetailsColumns);
        Assert.Equal(BrowserViewMode.Tiles, view.ViewMode);
        Assert.True(view.IsTilesMode);

        view.ViewMode = BrowserViewMode.List;
        Assert.False(view.IsTilesMode);
    }

    [Fact]
    public void BrowserDetailsColumn_DefaultsAndInitValues_AreApplied()
    {
        var defaults = new BrowserDetailsColumn
        {
            Header = "Title",
            ValuePath = "Path"
        };

        Assert.Equal(new GridLength(1, GridUnitType.Star), defaults.Width);
        Assert.Equal(HorizontalAlignment.Left, defaults.HeaderAlignment);
        Assert.Equal(HorizontalAlignment.Left, defaults.ValueAlignment);

        var configured = new BrowserDetailsColumn
        {
            Header = "Size",
            ValuePath = "DisplaySize",
            Width = new GridLength(140, GridUnitType.Pixel),
            HeaderAlignment = HorizontalAlignment.Center,
            ValueAlignment = HorizontalAlignment.Right
        };

        Assert.Equal("Size", configured.Header);
        Assert.Equal("DisplaySize", configured.ValuePath);
        Assert.Equal(new GridLength(140, GridUnitType.Pixel), configured.Width);
        Assert.Equal(HorizontalAlignment.Center, configured.HeaderAlignment);
        Assert.Equal(HorizontalAlignment.Right, configured.ValueAlignment);
    }

    [Fact]
    public void BrowserViewMode_EnumValues_AreStable()
    {
        Assert.Equal(0, (int)BrowserViewMode.Tiles);
        Assert.Equal(1, (int)BrowserViewMode.SmallIcons);
        Assert.Equal(2, (int)BrowserViewMode.LargeIcons);
        Assert.Equal(3, (int)BrowserViewMode.List);
        Assert.Equal(4, (int)BrowserViewMode.Details);
    }

    [Fact]
    public void StatusBar_ExposesBindableStatusAndProgressProperties()
    {
        var statusBar = new StatusBar
        {
            StatusText = "Ready",
            ProgressText = "50%",
            IsProgressVisible = true,
            ProgressValue = 50,
            ProgressMin = 10,
            ProgressMax = 90
        };

        Assert.Equal("Ready", statusBar.StatusText);
        Assert.Equal("50%", statusBar.ProgressText);
        Assert.True(statusBar.IsProgressVisible);
        Assert.Equal(50, statusBar.ProgressValue);
        Assert.Equal(10, statusBar.ProgressMin);
        Assert.Equal(90, statusBar.ProgressMax);
    }
}
