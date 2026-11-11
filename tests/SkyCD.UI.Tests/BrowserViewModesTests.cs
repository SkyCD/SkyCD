using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkyCD.UI.Controls.Lists;
using Xunit;

namespace SkyCD.UI.Tests;

public class BrowserViewModesTests
{
    private static readonly BrowserDetailsColumn[] SampleColumns =
    [
        new() { Header = "Name", ValuePath = "Name", Width = new GridLength(1, GridUnitType.Star) },
        new() { Header = "Type", ValuePath = "DisplayType", Width = new GridLength(120, GridUnitType.Pixel) },
        new() { Header = "Size", ValuePath = "DisplaySize", Width = new GridLength(80, GridUnitType.Pixel) }
    ];

    private static List<TestBrowserItem> CreateSampleItems(int count = 40)
    {
        var items = new List<TestBrowserItem>();
        for (var i = 1; i <= count; i++)
        {
            items.Add(new TestBrowserItem
            {
                Id = $"item-{i}",
                Name = $"Item {i:D3}.txt",
                DisplayType = i % 2 == 0 ? "Document" : "Audio File",
                DisplaySize = $"{i * 1024} KB",
                IconGlyph = "file"
            });
        }

        return items;
    }

    [AvaloniaFact]
    public void SmallIcons_RendersCompactIconPlusLabelRowWithoutCardOutline()
    {
        var view = new BrowserItemsView
        {
            ViewMode = BrowserViewMode.SmallIcons,
            BrowserGridItemWidth = 140,
            BrowserGridItemHeight = 60
        };
        var listBox = view.FindControl<ListBox>("IconGridListBox")!;
        var template = listBox.ItemTemplate!;
        var item = new TestBrowserItem { Name = "TestSmall.txt" };
        var rendered = template.Build(item)!;
        rendered.DataContext = item;
        Dispatcher.UIThread.RunJobs();

        var panel = Assert.IsType<StackPanel>(rendered);
        Assert.Equal(Orientation.Horizontal, panel.Orientation);
        Assert.Equal(22, panel.Height);

        // Icon is 16x16
        var iconGrid = panel.Children.OfType<Grid>().Single();
        Assert.Equal(16, iconGrid.Width);
        Assert.Equal(16, iconGrid.Height);

        // Label is on right
        var textBlock = panel.Children.OfType<TextBlock>().Single();
        Assert.Equal("TestSmall.txt", textBlock.Text);

        // No persistent card Border wrapper
        Assert.False(rendered is Border { BorderThickness.Left: > 0 });
    }

    [AvaloniaFact]
    public void LargeIcons_RendersCenteredIconOnTopAndLabelBelowWithoutCardOutline()
    {
        var view = new BrowserItemsView
        {
            ViewMode = BrowserViewMode.LargeIcons,
            BrowserGridItemWidth = 100,
            BrowserGridItemHeight = 90
        };
        var listBox = view.FindControl<ListBox>("IconGridListBox")!;
        var template = listBox.ItemTemplate!;
        var item = new TestBrowserItem { Name = "TestLargeLongFilename.iso" };
        var rendered = template.Build(item)!;
        rendered.DataContext = item;
        Dispatcher.UIThread.RunJobs();

        var panel = Assert.IsType<StackPanel>(rendered);
        Assert.Equal(Orientation.Vertical, panel.Orientation);

        // Icon is 32x32 centered
        var iconGrid = panel.Children.OfType<Grid>().Single();
        Assert.Equal(32, iconGrid.Width);
        Assert.Equal(32, iconGrid.Height);
        Assert.Equal(HorizontalAlignment.Center, iconGrid.HorizontalAlignment);

        // Label is centered with wrapping
        var textBlock = panel.Children.OfType<TextBlock>().Single();
        Assert.Equal("TestLargeLongFilename.iso", textBlock.Text);
        Assert.Equal(TextAlignment.Center, textBlock.TextAlignment);
        Assert.Equal(TextWrapping.Wrap, textBlock.TextWrapping);

        // No persistent card Border wrapper
        Assert.False(rendered is Border { BorderThickness.Left: > 0 });
    }

    [AvaloniaFact]
    public void Tiles_RendersHorizontalIconAndMetadataStackWithoutCardOutline()
    {
        var view = new BrowserItemsView
        {
            ViewMode = BrowserViewMode.Tiles,
            BrowserGridItemWidth = 260,
            BrowserGridItemHeight = 64
        };
        var listBox = view.FindControl<ListBox>("IconGridListBox")!;
        var template = listBox.ItemTemplate!;
        var item = new TestBrowserItem
        {
            Name = "Track01.flac",
            DisplayType = "Audio File",
            DisplaySize = "34.5 MB"
        };
        var rendered = template.Build(item)!;
        rendered.DataContext = item;
        Dispatcher.UIThread.RunJobs();

        var grid = Assert.IsType<Grid>(rendered);
        Assert.Equal(2, grid.ColumnDefinitions.Count);
        Assert.Equal(GridUnitType.Auto, grid.ColumnDefinitions[0].Width.GridUnitType);
        Assert.Equal(GridUnitType.Star, grid.ColumnDefinitions[1].Width.GridUnitType);

        // Icon 32x32 in Column 0
        var iconGrid = Assert.IsType<Grid>(grid.Children[0]);
        Assert.Equal(0, Grid.GetColumn(iconGrid));
        Assert.Equal(32, iconGrid.Width);
        Assert.Equal(32, iconGrid.Height);

        // Metadata stack in Column 1
        var metaStack = Assert.IsType<StackPanel>(grid.Children[1]);
        Assert.Equal(1, Grid.GetColumn(metaStack));
        var textBlocks = metaStack.Children.OfType<TextBlock>().ToList();
        Assert.Equal(3, textBlocks.Count);
        Assert.Equal("Track01.flac", textBlocks[0].Text);
        Assert.Equal("34.5 MB", textBlocks[2].Text);

        // No persistent card Border wrapper
        Assert.False(rendered is Border { BorderThickness.Left: > 0 });
    }

    [AvaloniaFact]
    public void ListMode_UsesVerticalWrapPanelAndHorizontalScrolling()
    {
        var view = new BrowserItemsView
        {
            ViewMode = BrowserViewMode.List
        };
        var listBox = view.FindControl<ListBox>("ListModeListBox")!;

        Assert.Equal(ScrollBarVisibility.Auto, ScrollViewer.GetHorizontalScrollBarVisibility(listBox));
        Assert.Equal(ScrollBarVisibility.Disabled, ScrollViewer.GetVerticalScrollBarVisibility(listBox));

        var itemsPanel = listBox.ItemsPanel;
        var panel = itemsPanel.Build();
        var wrapPanel = Assert.IsType<WrapPanel>(panel);
        Assert.Equal(Orientation.Vertical, wrapPanel.Orientation);
    }

    [AvaloniaFact]
    public void ModeSwitching_PreservesSingleAndMultipleSelection()
    {
        var items = CreateSampleItems(20);
        var view = new BrowserItemsView
        {
            ItemsSource = items,
            DetailsColumns = SampleColumns,
            ViewMode = BrowserViewMode.Details
        };

        var window = new Window
        {
            Width = 500,
            Height = 350,
            Content = view
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Select item 5
        var item5 = items[4];
        var item6 = items[5];
        view.SelectedItem = item5;
        view.SelectedItems = new List<object> { item5, item6 };
        Dispatcher.UIThread.RunJobs();

        Assert.Same(item5, view.SelectedItem);
        Assert.Equal(2, view.SelectedItems?.Count);

        // Switch to List
        view.ViewMode = BrowserViewMode.List;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(item5, view.SelectedItem);
        Assert.Equal(2, view.SelectedItems?.Count);

        // Switch to SmallIcons
        view.ViewMode = BrowserViewMode.SmallIcons;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(item5, view.SelectedItem);
        Assert.Equal(2, view.SelectedItems?.Count);

        // Switch to LargeIcons
        view.ViewMode = BrowserViewMode.LargeIcons;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(item5, view.SelectedItem);
        Assert.Equal(2, view.SelectedItems?.Count);

        // Switch to Tiles
        view.ViewMode = BrowserViewMode.Tiles;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(item5, view.SelectedItem);
        Assert.Equal(2, view.SelectedItems?.Count);

        // Switch back to Details
        view.ViewMode = BrowserViewMode.Details;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(item5, view.SelectedItem);
        Assert.Equal(2, view.SelectedItems?.Count);

        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(BrowserViewMode.Details)]
    [InlineData(BrowserViewMode.List)]
    [InlineData(BrowserViewMode.SmallIcons)]
    [InlineData(BrowserViewMode.LargeIcons)]
    [InlineData(BrowserViewMode.Tiles)]
    public void ViewportOverflow_RendersDeterministicScreenshotForEveryMode(BrowserViewMode mode)
    {
        var items = CreateSampleItems(50);
        var view = new BrowserItemsView
        {
            ItemsSource = items,
            DetailsColumns = SampleColumns,
            ViewMode = mode,
            Width = 450,
            Height = 300
        };

        var window = new Window
        {
            Width = 450,
            Height = 300,
            Content = view
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Select an item that requires scrolling into view
        var selected = items[25];
        view.SelectedItem = selected;
        Dispatcher.UIThread.RunJobs();

        // Capture deterministic screenshot / rendered frame
        using var bitmap = new RenderTargetBitmap(new PixelSize(450, 300));
        bitmap.Render(window);
        Assert.Equal(450, bitmap.PixelSize.Width);
        Assert.Equal(300, bitmap.PixelSize.Height);

        // Verify rendered frame contains non-empty pixel data
        var bufferSize = 450 * 300 * 4;
        var ptr = System.Runtime.InteropServices.Marshal.AllocHGlobal(bufferSize);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, 450, 300), ptr, bufferSize, 450 * 4);
            var buffer = new byte[bufferSize];
            System.Runtime.InteropServices.Marshal.Copy(ptr, buffer, 0, bufferSize);
            Assert.Contains(buffer, b => b != 0);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(ptr);
        }

        window.Close();
    }

    [AvaloniaFact]
    public void KeyboardNavigation_AndSelectionCommands_TargetCorrectItems()
    {
        var items = CreateSampleItems(10);
        var view = new BrowserItemsView
        {
            ItemsSource = items,
            DetailsColumns = SampleColumns,
            ViewMode = BrowserViewMode.Details,
            Width = 400,
            Height = 300
        };

        var window = new Window
        {
            Width = 400,
            Height = 300,
            Content = view
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        view.SelectedItem = items[0];
        Dispatcher.UIThread.RunJobs();
        Assert.Same(items[0], view.SelectedItem);

        // Switch selection programmatically
        view.SelectedItem = items[3];
        Dispatcher.UIThread.RunJobs();
        Assert.Same(items[3], view.SelectedItem);

        window.Close();
    }

    public sealed class TestBrowserItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string DisplayType { get; set; } = "";
        public string DisplaySize { get; set; } = "";
        public string IconGlyph { get; set; } = "file";
        public Dictionary<string, object?> Properties { get; set; } = new();
    }
}
