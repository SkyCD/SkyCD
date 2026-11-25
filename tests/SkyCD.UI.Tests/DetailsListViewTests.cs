using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
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

        // Column 0 has icon gutter + the name cell that swaps its label for a rename editor
        var col0Panel = Assert.IsType<DockPanel>(rowGrid.Children[0]);
        var col0Grids = col0Panel.Children.OfType<Grid>().ToArray();
        Assert.Equal(2, col0Grids.Length);
        var icon = col0Grids[0];
        Assert.Equal(16, icon.Width);
        Assert.Equal(16, icon.Height);

        var nameCell = col0Grids[1];
        var nameText = nameCell.Children.OfType<TextBlock>().Single();
        Assert.Equal(TextTrimming.CharacterEllipsis, nameText.TextTrimming);
        Assert.True(nameText.ClipToBounds);
        Assert.Contains(nameCell.Children, child => child is TextBox editor && editor.Classes.Contains("rename-editor"));

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
    public void CatalogColumns_CarryIdRequiredFlagAndSavedPixelWidth()
    {
        var settings = new List<DetailsColumnSettingDocument>
        {
            new() { Id = DetailsColumnsCatalog.NameId, IsVisible = true, Width = 640 },
            new() { Id = DetailsColumnsCatalog.SizeId, IsVisible = true, Width = 175 },
            new() { Id = DetailsColumnsCatalog.TypeId, IsVisible = false },
            new() { Id = DetailsColumnsCatalog.StatusId, IsVisible = true, Width = 140 }
        };

        var columns = DetailsColumnsCatalog.CreateVisibleColumns(settings);

        Assert.Equal(
        [
            DetailsColumnsCatalog.NameId,
            DetailsColumnsCatalog.SizeId,
            DetailsColumnsCatalog.StatusId
        ], columns.Select(column => column.Id));
        Assert.True(columns[0].IsRequired);
        Assert.False(columns[1].IsRequired);
        Assert.Equal(new GridLength(640, GridUnitType.Pixel), columns[0].Width);
        Assert.Equal(new GridLength(175, GridUnitType.Pixel), columns[1].Width);
        Assert.Equal(new GridLength(140, GridUnitType.Pixel), columns[2].Width);

        var view = new BrowserItemsView
        {
            DetailsColumns = columns,
            ViewMode = BrowserViewMode.Details
        };
        var detailsListView = view.FindControl<DetailsListView>("DetailsListView")!;
        var headerGrid = Assert.IsType<Grid>(detailsListView.HeaderContent);
        Assert.Equal(new GridLength(175, GridUnitType.Pixel), headerGrid.ColumnDefinitions[1].Width);
    }

    [AvaloniaFact]
    public void SplitterDrag_RaisesColumnResizedWithNewWidth()
    {
        var (window, view, headerGrid) = ShowDetailsColumns(
            CreateCatalogColumn(DetailsColumnsCatalog.NameId, required: true, width: 300),
            CreateCatalogColumn(DetailsColumnsCatalog.SizeId, required: false, width: 100));

        try
        {
            var raised = new List<DetailsColumnResizedEventArgs>();
            view.DetailsColumnResized += (_, e) => raised.Add(e);

            var sizeCell = Assert.IsType<Grid>(headerGrid.Children[1]);
            var splitter = sizeCell.GetVisualChildren().OfType<Border>().Last(border => border.Cursor is not null);
            var grip = splitter.TranslatePoint(new Point(4, 10), window);
            Assert.NotNull(grip);

            // A plain click on the splitter must not report a resize.
            window.MouseDown(grip!.Value, MouseButton.Left);
            window.MouseUp(grip.Value, MouseButton.Left);
            Assert.Empty(raised);

            var sizeColumn = view.DetailsColumns!.ElementAt(1);
            window.MouseDown(grip.Value, MouseButton.Left);
            window.MouseMove(grip.Value + new Vector(60, 0), RawInputModifiers.LeftMouseButton);
            window.MouseUp(grip.Value + new Vector(60, 0), MouseButton.Left);

            var resized = Assert.Single(raised);
            Assert.Equal(DetailsColumnsCatalog.SizeId, resized.ColumnId);
            Assert.Equal(160, resized.Width, 3);
            Assert.Equal(new GridLength(160, GridUnitType.Pixel), sizeColumn.Width);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void HeaderDrag_RaisesReorderedWithTargetIndex()
    {
        var (window, view, headerGrid) = ShowDetailsColumns(
            CreateCatalogColumn(DetailsColumnsCatalog.NameId, required: true, width: 150),
            CreateCatalogColumn(DetailsColumnsCatalog.SizeId, required: false, width: 120),
            CreateCatalogColumn(DetailsColumnsCatalog.TypeId, required: false, width: 100),
            CreateCatalogColumn(DetailsColumnsCatalog.StatusId, required: false, width: 140));

        try
        {
            var reordered = new List<DetailsColumnReorderedEventArgs>();
            view.DetailsColumnReordered += (_, e) => reordered.Add(e);

            // Drag the Size header onto the Status header.
            var sizeCell = Assert.IsType<Grid>(headerGrid.Children[1]);
            var statusCell = Assert.IsType<Grid>(headerGrid.Children[3]);
            var start = sizeCell.TranslatePoint(new Point(30, 10), window);
            var drop = statusCell.TranslatePoint(new Point(30, 10), window);
            Assert.NotNull(start);
            Assert.NotNull(drop);

            window.MouseDown(start!.Value, MouseButton.Left);
            window.MouseMove(start.Value + new Vector(10, 0), RawInputModifiers.LeftMouseButton);
            window.MouseMove(drop!.Value, RawInputModifiers.LeftMouseButton);
            window.MouseUp(drop.Value, MouseButton.Left);

            var move = Assert.Single(reordered);
            Assert.Equal(DetailsColumnsCatalog.SizeId, move.ColumnId);
            Assert.Equal(3, move.TargetIndex);

            // A plain click on a reorderable header is not a reorder.
            reordered.Clear();
            var typeCell = Assert.IsType<Grid>(headerGrid.Children[2]);
            var typePoint = typeCell.TranslatePoint(new Point(30, 10), window);
            Assert.NotNull(typePoint);
            window.MouseDown(typePoint!.Value, MouseButton.Left);
            window.MouseUp(typePoint.Value, MouseButton.Left);
            Assert.Empty(reordered);

            // Dropping inside the dragged header itself is not a reorder.
            window.MouseDown(typePoint.Value, MouseButton.Left);
            window.MouseMove(typePoint.Value + new Vector(10, 0), RawInputModifiers.LeftMouseButton);
            window.MouseMove(typePoint.Value + new Vector(3, 0), RawInputModifiers.LeftMouseButton);
            window.MouseUp(typePoint.Value + new Vector(3, 0), MouseButton.Left);
            Assert.Empty(reordered);

            // Dropping before the leading column places it at index 0.
            var namePoint = headerGrid.TranslatePoint(new Point(10, 10), window);
            Assert.NotNull(namePoint);
            window.MouseDown(typePoint.Value, MouseButton.Left);
            window.MouseMove(typePoint.Value + new Vector(-20, 0), RawInputModifiers.LeftMouseButton);
            window.MouseMove(namePoint!.Value, RawInputModifiers.LeftMouseButton);
            window.MouseUp(namePoint.Value, MouseButton.Left);

            move = Assert.Single(reordered);
            Assert.Equal(DetailsColumnsCatalog.TypeId, move.ColumnId);
            Assert.Equal(0, move.TargetIndex);

            // The required Name header is draggable like every other header.
            reordered.Clear();
            window.MouseDown(namePoint.Value, MouseButton.Left);
            window.MouseMove(namePoint.Value + new Vector(10, 0), RawInputModifiers.LeftMouseButton);
            window.MouseMove(drop.Value, RawInputModifiers.LeftMouseButton);
            window.MouseUp(drop.Value, MouseButton.Left);

            move = Assert.Single(reordered);
            Assert.Equal(DetailsColumnsCatalog.NameId, move.ColumnId);
            Assert.Equal(3, move.TargetIndex);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void HeaderClick_RaisesColumnClickedButPlainSplitterClickDoesNot()
    {
        var (window, view, headerGrid) = ShowDetailsColumns(
            CreateCatalogColumn(DetailsColumnsCatalog.NameId, required: true, width: 200),
            CreateCatalogColumn(DetailsColumnsCatalog.SizeId, required: false, width: 120));

        try
        {
            var clicked = new List<DetailsColumnClickedEventArgs>();
            view.DetailsColumnClicked += (_, e) => clicked.Add(e);

            var sizeCell = Assert.IsType<Grid>(headerGrid.Children[1]);
            var sizePoint = sizeCell.TranslatePoint(new Point(30, 10), window);
            Assert.NotNull(sizePoint);
            window.MouseDown(sizePoint!.Value, MouseButton.Left);
            window.MouseUp(sizePoint.Value, MouseButton.Left);

            var click = Assert.Single(clicked);
            Assert.Equal(DetailsColumnsCatalog.SizeId, click.ColumnId);

            // The resize grip owns the press, so releasing it must not be read as a header click.
            clicked.Clear();
            var splitter = sizeCell.GetVisualChildren().OfType<Border>().Last(border => border.Cursor is not null);
            var grip = splitter.TranslatePoint(new Point(4, 10), window);
            Assert.NotNull(grip);
            window.MouseDown(grip!.Value, MouseButton.Left);
            window.MouseUp(grip.Value, MouseButton.Left);
            Assert.Empty(clicked);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void HeaderDrag_DoesNotRaiseColumnClicked()
    {
        var (window, view, headerGrid) = ShowDetailsColumns(
            CreateCatalogColumn(DetailsColumnsCatalog.NameId, required: true, width: 150),
            CreateCatalogColumn(DetailsColumnsCatalog.SizeId, required: false, width: 120),
            CreateCatalogColumn(DetailsColumnsCatalog.StatusId, required: false, width: 140));

        try
        {
            var clicked = new List<DetailsColumnClickedEventArgs>();
            var reordered = new List<DetailsColumnReorderedEventArgs>();
            view.DetailsColumnClicked += (_, e) => clicked.Add(e);
            view.DetailsColumnReordered += (_, e) => reordered.Add(e);

            var sizeCell = Assert.IsType<Grid>(headerGrid.Children[1]);
            var statusCell = Assert.IsType<Grid>(headerGrid.Children[2]);
            var start = sizeCell.TranslatePoint(new Point(30, 10), window);
            var drop = statusCell.TranslatePoint(new Point(30, 10), window);
            Assert.NotNull(start);
            Assert.NotNull(drop);

            window.MouseDown(start!.Value, MouseButton.Left);
            window.MouseMove(start.Value + new Vector(10, 0), RawInputModifiers.LeftMouseButton);
            window.MouseMove(drop!.Value, RawInputModifiers.LeftMouseButton);
            window.MouseUp(drop.Value, MouseButton.Left);

            Assert.Single(reordered);
            Assert.Empty(clicked);

            // A press that passes the drag threshold and returns to its origin is still a drag.
            window.MouseDown(start.Value, MouseButton.Left);
            window.MouseMove(start.Value + new Vector(20, 0), RawInputModifiers.LeftMouseButton);
            window.MouseMove(start.Value, RawInputModifiers.LeftMouseButton);
            window.MouseUp(start.Value, MouseButton.Left);

            Assert.Empty(clicked);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void HeaderContent_MarksActiveSortColumnWithDirectionArrow()
    {
        var (window, view, headerGrid) = ShowDetailsColumns(
            CreateCatalogColumn(DetailsColumnsCatalog.NameId, required: true, width: 200),
            CreateCatalogColumn(DetailsColumnsCatalog.SizeId, required: false, width: 120));

        try
        {
            var initialSizeCell = Assert.IsType<Grid>(headerGrid.Children[1]);
            Assert.DoesNotContain(initialSizeCell.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text is "▲" or "▼");

            view.SortColumnId = DetailsColumnsCatalog.SizeId;
            var detailsListView = view.FindControl<DetailsListView>("DetailsListView")!;

            var ascendingHeader = Assert.IsType<Grid>(detailsListView.HeaderContent);
            var ascendingSizeCell = Assert.IsType<Grid>(ascendingHeader.Children[1]);
            Assert.Contains(ascendingSizeCell.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "▲");
            var nameCell = Assert.IsType<Grid>(ascendingHeader.Children[0]);
            Assert.DoesNotContain(nameCell.GetVisualDescendants().OfType<TextBlock>(), text => text.Text is "▲" or "▼");

            view.IsSortDescending = true;
            var descendingHeader = Assert.IsType<Grid>(detailsListView.HeaderContent);
            var descendingSizeCell = Assert.IsType<Grid>(descendingHeader.Children[1]);
            Assert.Contains(descendingSizeCell.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "▼");
            Assert.DoesNotContain(descendingSizeCell.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "▲");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void HeaderDrag_ShowsDropMarkerAndGhost_AndClearsOnRelease()
    {
        var (window, view, headerGrid) = ShowDetailsColumns(
            CreateCatalogColumn(DetailsColumnsCatalog.NameId, required: true, width: 150),
            CreateCatalogColumn(DetailsColumnsCatalog.SizeId, required: false, width: 120),
            CreateCatalogColumn(DetailsColumnsCatalog.TypeId, required: false, width: 100),
            CreateCatalogColumn(DetailsColumnsCatalog.StatusId, required: false, width: 140));

        try
        {
            var marker = FindHeaderVisual(headerGrid, "DetailsHeaderDropMarker");
            var ghost = FindHeaderVisual(headerGrid, "DetailsHeaderDragGhost");
            var nameCell = Assert.IsType<Grid>(headerGrid.Children[0]);
            var sizeCell = Assert.IsType<Grid>(headerGrid.Children[1]);
            var typeCell = Assert.IsType<Grid>(headerGrid.Children[2]);
            var statusCell = Assert.IsType<Grid>(headerGrid.Children[3]);

            Assert.False(marker.IsVisible);
            Assert.False(ghost.IsVisible);

            // A plain click and travel inside the 4 px threshold show no drag visuals.
            var start = sizeCell.TranslatePoint(new Point(30, 10), window)!.Value;
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(2, 0), RawInputModifiers.LeftMouseButton);
            Assert.False(marker.IsVisible);
            Assert.False(ghost.IsVisible);
            window.MouseUp(start + new Vector(2, 0), MouseButton.Left);
            Assert.False(marker.IsVisible);
            Assert.False(ghost.IsVisible);

            // Once past the threshold, the marker sits at the drop cell's leading edge.
            var reordered = new List<DetailsColumnReorderedEventArgs>();
            view.DetailsColumnReordered += (_, e) => reordered.Add(e);
            var typePoint = typeCell.TranslatePoint(new Point(30, 10), window)!.Value;
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(typePoint, RawInputModifiers.LeftMouseButton);

            Assert.True(marker.IsVisible);
            Assert.True(ghost.IsVisible);
            Assert.Equal(DetailsColumnsCatalog.SizeId, Assert.IsType<TextBlock>(ghost.Child).Text);
            Assert.Equal(typeCell.Bounds.Left, Canvas.GetLeft(marker), 3);

            // Dragging over the leading column places the marker at its leading edge.
            var namePoint = nameCell.TranslatePoint(new Point(10, 10), window)!.Value;
            window.MouseMove(namePoint, RawInputModifiers.LeftMouseButton);
            Assert.True(marker.IsVisible);
            Assert.Equal(nameCell.Bounds.Left, Canvas.GetLeft(marker), 3);

            // The marker tracks the pointer to the far column.
            var statusPoint = statusCell.TranslatePoint(new Point(30, 10), window)!.Value;
            window.MouseMove(statusPoint, RawInputModifiers.LeftMouseButton);
            Assert.Equal(statusCell.Bounds.Left, Canvas.GetLeft(marker), 3);

            window.MouseUp(statusPoint, MouseButton.Left);
            Assert.False(marker.IsVisible);
            Assert.False(ghost.IsVisible);

            var move = Assert.Single(reordered);
            Assert.Equal(DetailsColumnsCatalog.SizeId, move.ColumnId);
            Assert.Equal(3, move.TargetIndex);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void HeaderDrag_GhostIsOpaqueSurfaceCopyOfTheHeaderCell()
    {
        var (window, _, headerGrid) = ShowDetailsColumns(
            CreateCatalogColumn(DetailsColumnsCatalog.NameId, required: true, width: 150),
            CreateCatalogColumn(DetailsColumnsCatalog.SizeId, required: false, width: 120));

        try
        {
            var ghost = FindHeaderVisual(headerGrid, "DetailsHeaderDragGhost");
            var nameCell = Assert.IsType<Grid>(headerGrid.Children[0]);
            var sizeCell = Assert.IsType<Grid>(headerGrid.Children[1]);

            var start = sizeCell.TranslatePoint(new Point(30, 10), window)!.Value;
            var namePoint = nameCell.TranslatePoint(new Point(10, 10), window)!.Value;
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(namePoint, RawInputModifiers.LeftMouseButton);
            Assert.True(ghost.IsVisible);

            // Issue #1112: the ghost must hide the headers underneath it, so it is a solid theme
            // surface at full opacity, sized like the header cell it copies, with a lift shadow.
            Assert.Equal(1d, ghost.Opacity, 3);
            Assert.NotEqual(default, ghost.BoxShadow);
            Assert.Equal(sizeCell.Bounds.Width, ghost.Width, 1);

            var ghostSurface = Assert.IsAssignableFrom<ISolidColorBrush>(ghost.Background);
            Assert.Equal(byte.MaxValue, ghostSurface.Color.A);

            var headerSurface = Assert.IsAssignableFrom<ISolidColorBrush>(
                Assert.IsType<Border>(sizeCell.Children[0]).Background);
            Assert.Equal(byte.MaxValue, headerSurface.Color.A);
            Assert.Equal(headerSurface.Color, ghostSurface.Color);

            window.MouseUp(namePoint, MouseButton.Left);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void HeaderDrag_CaptureLost_ClearsDragVisuals()
    {
        var (window, _, headerGrid) = ShowDetailsColumns(
            CreateCatalogColumn(DetailsColumnsCatalog.NameId, required: true, width: 150),
            CreateCatalogColumn(DetailsColumnsCatalog.SizeId, required: false, width: 120),
            CreateCatalogColumn(DetailsColumnsCatalog.TypeId, required: false, width: 100));

        try
        {
            var marker = FindHeaderVisual(headerGrid, "DetailsHeaderDropMarker");
            var ghost = FindHeaderVisual(headerGrid, "DetailsHeaderDragGhost");
            var sizeCell = Assert.IsType<Grid>(headerGrid.Children[1]);
            var typeCell = Assert.IsType<Grid>(headerGrid.Children[2]);

            var start = sizeCell.TranslatePoint(new Point(30, 10), window)!.Value;
            var typePoint = typeCell.TranslatePoint(new Point(30, 10), window)!.Value;
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(typePoint, RawInputModifiers.LeftMouseButton);
            Assert.True(marker.IsVisible);
            Assert.True(ghost.IsVisible);

            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            headerGrid.RaiseEvent(new PointerCaptureLostEventArgs(headerGrid, pointer));
            Dispatcher.UIThread.RunJobs();

            Assert.False(marker.IsVisible);
            Assert.False(ghost.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DetailHeaders_RequiredNameShowsDragCursor()
    {
        var (window, _, headerGrid) = ShowDetailsColumns(
            CreateCatalogColumn(DetailsColumnsCatalog.NameId, required: true, width: 150),
            CreateCatalogColumn(DetailsColumnsCatalog.SizeId, required: false, width: 120));

        try
        {
            var headerCells = headerGrid.Children.OfType<Grid>().ToArray();
            Assert.Equal(2, headerCells.Length);
            Assert.All(headerCells, cell =>
            {
                var headerBorder = Assert.IsType<Border>(cell.Children[0]);
                Assert.NotNull(headerBorder.Cursor);
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DetailsRow_IconAndNameFollowNameColumnWhenItIsNotFirst()
    {
        var columns = new[]
        {
            new BrowserDetailsColumn
            {
                Id = DetailsColumnsCatalog.SizeId,
                Header = "Size",
                ValuePath = "DisplaySize",
                Width = new GridLength(100, GridUnitType.Pixel),
                ValueAlignment = HorizontalAlignment.Right
            },
            new BrowserDetailsColumn
            {
                Id = DetailsColumnsCatalog.NameId,
                Header = "Name",
                ValuePath = "Name",
                Width = new GridLength(500, GridUnitType.Pixel),
                IsRequired = true,
                ShowsItemIcon = true
            }
        };

        var view = new BrowserItemsView
        {
            DetailsColumns = columns,
            ViewMode = BrowserViewMode.Details
        };

        var detailsListView = view.FindControl<DetailsListView>("DetailsListView")!;
        var item = new BrowserViewModesTests.TestBrowserItem
        {
            Name = "Alpha.dat",
            DisplaySize = "1 KB"
        };

        var renderedRow = detailsListView.RowTemplate!.Build(item)!;
        renderedRow.DataContext = item;
        Dispatcher.UIThread.RunJobs();

        var rowGrid = Assert.IsType<Grid>(renderedRow);
        var sizeText = Assert.IsType<TextBlock>(rowGrid.Children[0]);
        Assert.Equal(0, Grid.GetColumn(sizeText));
        Assert.Equal(HorizontalAlignment.Right, sizeText.HorizontalAlignment);

        var namePanel = Assert.IsType<DockPanel>(rowGrid.Children[1]);
        Assert.Equal(1, Grid.GetColumn(namePanel));
        var iconContainer = Assert.IsType<Grid>(namePanel.Children[0]);
        Assert.Equal(16, iconContainer.Width);
        var nameCell = Assert.IsType<Grid>(namePanel.Children[1]);
        var nameText = Assert.IsType<TextBlock>(nameCell.Children[0]);
        Assert.Equal("Alpha.dat", nameText.Text);
    }

    private static Border FindHeaderVisual(Grid headerGrid, string name)
    {
        return Assert.Single(headerGrid.GetVisualDescendants().OfType<Border>(), border => border.Name == name);
    }

    private static BrowserDetailsColumn CreateCatalogColumn(string id, bool required, double width)
    {
        return new BrowserDetailsColumn
        {
            Id = id,
            Header = id,
            ValuePath = "Name",
            Width = new GridLength(width, GridUnitType.Pixel),
            IsRequired = required
        };
    }

    private static (Window Window, BrowserItemsView View, Grid HeaderGrid) ShowDetailsColumns(
        params BrowserDetailsColumn[] columns)
    {
        var view = new BrowserItemsView
        {
            DetailsColumns = columns,
            ItemsSource = CreateSeededItems(5),
            ViewMode = BrowserViewMode.Details
        };
        var window = new Window { Width = 900, Height = 400, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var detailsListView = view.FindControl<DetailsListView>("DetailsListView")!;
        var headerGrid = Assert.IsType<Grid>(detailsListView.HeaderContent);
        Assert.True(headerGrid.Bounds.Width > 0, "the details header must be laid out");

        return (window, view, headerGrid);
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
