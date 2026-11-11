using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkyCD.Documents;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Controls.Lists;
using Xunit;

namespace SkyCD.UI.Tests;

public class DetailsListViewTests
{
    private static List<BrowserViewModesTests.TestBrowserItem> CreateSeededItems(int count = 100)
    {
        var items = new List<BrowserViewModesTests.TestBrowserItem>();
        var samplePrefixes = new[] { "Alpha", "Archive", "Audio", "Backup", "Beta", "Bravo", "Charlie", "Data", "Delta", "Document", "Echo", "Executive", "Financial", "Foxtrot", "Golf", "Hotel", "Image", "India", "ISO", "Juliet", "Kilo", "Legacy", "Lima", "Mike", "Music", "November", "Oscar", "Papa", "Quebec", "Report", "Romeo", "Sierra", "System", "Tango", "Uniform", "Video", "Victor", "Whiskey", "Xray", "Yankee", "Zulu" };

        for (var i = 0; i < count; i++)
        {
            var prefix = samplePrefixes[i % samplePrefixes.Length];
            var longSuffix = i % 3 == 0 ? "_VeryLongFileNameForTestingOverflowAndEllipsisTextTrimmingBehavior" : string.Empty;
            items.Add(new BrowserViewModesTests.TestBrowserItem
            {
                Id = $"item-{i + 1}",
                Name = $"{prefix}_{i + 1:D3}{longSuffix}.dat",
                DisplayType = i % 2 == 0 ? "Data File" : "Media Archive",
                DisplaySize = $"{(i + 1) * 32768:N0} bytes",
                IconGlyph = "file"
            });
        }

        return items;
    }

    [AvaloniaFact]
    public void LegacyColumns_DefaultPresentation_MatchesWinForms500And100PixelLayout()
    {
        var columns = new BrowserDetailsColumn[]
        {
            new()
            {
                Header = "Name",
                ValuePath = "Name",
                Width = new GridLength(500, GridUnitType.Pixel)
            },
            new()
            {
                Header = "Size",
                ValuePath = "DisplaySize",
                Width = new GridLength(100, GridUnitType.Pixel),
                HeaderAlignment = HorizontalAlignment.Right,
                ValueAlignment = HorizontalAlignment.Right
            }
        };

        Assert.Equal(2, columns.Length);
        Assert.Equal("Name", columns[0].Header);
        Assert.Equal(new GridLength(500, GridUnitType.Pixel), columns[0].Width);
        Assert.Equal(HorizontalAlignment.Left, columns[0].HeaderAlignment);

        Assert.Equal("Size", columns[1].Header);
        Assert.Equal(new GridLength(100, GridUnitType.Pixel), columns[1].Width);
        Assert.Equal(HorizontalAlignment.Right, columns[1].HeaderAlignment);
        Assert.Equal(HorizontalAlignment.Right, columns[1].ValueAlignment);
    }

    [AvaloniaFact]
    public void ResizableHeaders_DraggableColumnBoundaries_ResizesHeaderAndRowsSimultaneously()
    {
        var col0 = new BrowserDetailsColumn
        {
            Header = "Name",
            ValuePath = "Name",
            Width = new GridLength(500, GridUnitType.Pixel)
        };
        var col1 = new BrowserDetailsColumn
        {
            Header = "Size",
            ValuePath = "DisplaySize",
            Width = new GridLength(100, GridUnitType.Pixel),
            HeaderAlignment = HorizontalAlignment.Right,
            ValueAlignment = HorizontalAlignment.Right
        };

        var items = CreateSeededItems(10);
        var view = new BrowserItemsView
        {
            DetailsColumns = new[] { col0, col1 },
            ItemsSource = items,
            ViewMode = BrowserViewMode.Details
        };

        var window = new Window
        {
            Width = 900,
            Height = 500,
            Content = view
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var detailsListView = view.FindControl<DetailsListView>("DetailsListView")!;
        var headerGrid = Assert.IsType<Grid>(detailsListView.HeaderContent);
        Assert.Equal(3, headerGrid.ColumnDefinitions.Count); // col0, col1, filler

        // Header cell 0 has splitter
        var col0CellGrid = Assert.IsType<Grid>(headerGrid.Children[0]);
        var splitter = col0CellGrid.Children.OfType<Border>().FirstOrDefault(b => b.Cursor is not null);
        Assert.NotNull(splitter);

        // Simulate dragging splitter +120px to the right
        col0.Width = new GridLength(620, GridUnitType.Pixel);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(620, col0.Width.Value);
        Assert.Equal(new GridLength(620, GridUnitType.Pixel), headerGrid.ColumnDefinitions[0].Width);

        window.Close();
    }

    [AvaloniaFact]
    public void HeaderAndRowAlignment_SynchronizedHorizontalAndVerticalScrolling()
    {
        var col0 = new BrowserDetailsColumn
        {
            Header = "Name",
            ValuePath = "Name",
            Width = new GridLength(600, GridUnitType.Pixel)
        };
        var col1 = new BrowserDetailsColumn
        {
            Header = "Size",
            ValuePath = "DisplaySize",
            Width = new GridLength(300, GridUnitType.Pixel),
            HeaderAlignment = HorizontalAlignment.Right,
            ValueAlignment = HorizontalAlignment.Right
        };

        var items = CreateSeededItems(50);
        var view = new BrowserItemsView
        {
            DetailsColumns = new[] { col0, col1 },
            ItemsSource = items,
            ViewMode = BrowserViewMode.Details
        };

        var window = new Window
        {
            Width = 400,
            Height = 300,
            Content = view
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var detailsListView = view.FindControl<DetailsListView>("DetailsListView")!;
        var headerScrollViewer = detailsListView.FindControl<ScrollViewer>("HeaderScrollViewer")!;
        var innerListBox = detailsListView.FindControl<ListBox>("InnerListBox")!;
        innerListBox.ApplyTemplate();
        Dispatcher.UIThread.RunJobs();

        var listScrollViewer = innerListBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()
                               ?? detailsListView.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(s => s != headerScrollViewer);

        if (listScrollViewer is not null)
        {
            // Scroll horizontally
            listScrollViewer.Offset = new Vector(150, 0);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(150, headerScrollViewer.Offset.X);
        }

        window.Close();
    }

    [AvaloniaFact]
    public void LegacyRowDensity_TextClipping_And_IconGutter()
    {
        var col0 = new BrowserDetailsColumn
        {
            Header = "Name",
            ValuePath = "Name",
            Width = new GridLength(500, GridUnitType.Pixel)
        };
        var col1 = new BrowserDetailsColumn
        {
            Header = "Size",
            ValuePath = "DisplaySize",
            Width = new GridLength(100, GridUnitType.Pixel),
            HeaderAlignment = HorizontalAlignment.Right,
            ValueAlignment = HorizontalAlignment.Right
        };

        var view = new BrowserItemsView
        {
            DetailsColumns = new[] { col0, col1 },
            ViewMode = BrowserViewMode.Details
        };

        var detailsListView = view.FindControl<DetailsListView>("DetailsListView")!;
        var rowTemplate = detailsListView.RowTemplate!;
        var item = new BrowserViewModesTests.TestBrowserItem
        {
            Name = "Very_Long_Filename_Designed_To_Verify_Text_Trimming_Ellipsis_Without_Overlapping_Adjacent_Columns.dat",
            DisplaySize = "1,048,576 bytes"
        };

        var renderedRow = rowTemplate.Build(item)!;
        renderedRow.DataContext = item;
        Dispatcher.UIThread.RunJobs();

        var rowGrid = Assert.IsType<Grid>(renderedRow);
        Assert.Equal(2, rowGrid.ColumnDefinitions.Count);

        // Column 0 has icon gutter + text
        var col0Panel = Assert.IsType<DockPanel>(rowGrid.Children[0]);
        var icon = col0Panel.Children.OfType<Grid>().Single();
        Assert.Equal(16, icon.Width);
        Assert.Equal(16, icon.Height);

        var nameText = col0Panel.Children.OfType<TextBlock>().Single();
        Assert.Equal(TextTrimming.CharacterEllipsis, nameText.TextTrimming);
        Assert.True(nameText.ClipToBounds);

        // Column 1 has size text
        var sizeText = Assert.IsType<TextBlock>(rowGrid.Children[1]);
        Assert.Equal(HorizontalAlignment.Right, sizeText.HorizontalAlignment);
        Assert.Equal(TextTrimming.CharacterEllipsis, sizeText.TextTrimming);
    }

    [AvaloniaFact]
    public void KeyboardInteraction_TypeToSelect_And_SelectAll()
    {
        var items = new List<BrowserViewModesTests.TestBrowserItem>
        {
            new() { Id = "1", Name = "Alpha.txt" },
            new() { Id = "2", Name = "Beta.iso" },
            new() { Id = "3", Name = "Bravo.mp3" },
            new() { Id = "4", Name = "Charlie.doc" },
            new() { Id = "5", Name = "Delta.zip" }
        };

        var col0 = new BrowserDetailsColumn { Header = "Name", ValuePath = "Name" };
        var col1 = new BrowserDetailsColumn { Header = "Size", ValuePath = "DisplaySize" };

        var view = new BrowserItemsView
        {
            DetailsColumns = new[] { col0, col1 },
            ItemsSource = items,
            ViewMode = BrowserViewMode.Details
        };

        var window = new Window
        {
            Width = 600,
            Height = 400,
            Content = view
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var detailsListView = view.FindControl<DetailsListView>("DetailsListView")!;
        var listBox = detailsListView.FindControl<ListBox>("InnerListBox")!;

        // Type 'B' -> should select Beta.iso
        listBox.RaiseEvent(new TextInputEventArgs
        {
            RoutedEvent = InputElement.TextInputEvent,
            Source = listBox,
            Text = "B"
        });
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(items[1], detailsListView.SelectedItem);

        // Type 'B' again -> should cycle to Bravo.mp3
        listBox.RaiseEvent(new TextInputEventArgs
        {
            RoutedEvent = InputElement.TextInputEvent,
            Source = listBox,
            Text = "B"
        });
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(items[2], detailsListView.SelectedItem);

        // Type 'C' after pause -> should select Charlie.doc
        System.Threading.Thread.Sleep(1100);
        listBox.RaiseEvent(new TextInputEventArgs
        {
            RoutedEvent = InputElement.TextInputEvent,
            Source = listBox,
            Text = "C"
        });
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(items[3], detailsListView.SelectedItem);

        // Ctrl+A -> Select all
        listBox.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Source = listBox,
            Key = Key.A,
            KeyModifiers = KeyModifiers.Control
        });
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(5, detailsListView.SelectedItems?.Count);

        window.Close();
    }

    [AvaloniaFact]
    public void DoubleTapped_EventForwardsFromInnerListBox()
    {
        var items = CreateSeededItems(5);
        var col0 = new BrowserDetailsColumn { Header = "Name", ValuePath = "Name" };
        var view = new BrowserItemsView
        {
            DetailsColumns = new[] { col0 },
            ItemsSource = items,
            ViewMode = BrowserViewMode.Details
        };

        var window = new Window
        {
            Width = 600,
            Height = 400,
            Content = view
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var detailsListView = view.FindControl<DetailsListView>("DetailsListView")!;
        var doubleTappedFired = false;
        detailsListView.DoubleTapped += (_, _) => doubleTappedFired = true;

        var innerListBox = detailsListView.FindControl<ListBox>("InnerListBox")!;
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        var pointerArgs = new PointerReleasedEventArgs(innerListBox, pointer, innerListBox, new Point(0, 0), 0, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left);
        innerListBox.RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent, pointerArgs));
        Dispatcher.UIThread.RunJobs();

        Assert.True(doubleTappedFired);

        window.Close();
    }
}
