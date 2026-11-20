using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkyCD.App.Views;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Controls.Lists;
using SkyCD.UI.Controls.Properties;
using SkyCD.UI.Controls.StatusBar;
using SkyCD.UI.Controls.Toolbars;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.UI.Tests;

public class ControlsContractTests
{
    [AvaloniaFact]
    public void ClassicToolbar_Items_AcceptsToolbarInterfaceTypes()
    {
        var toolbar = new ClassicToolbar();

        toolbar.Items.Add(new ClassicToolbarButton());
        toolbar.Items.Add(new ClassicToolbarSeparator());

        Assert.Equal(2, toolbar.Items.Count);
        Assert.All(toolbar.Items, item => Assert.IsAssignableFrom<IClassicToolbarItem>(item));
    }

    [AvaloniaFact]
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

    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void PropertiesList_AlignsHeaderAndRowDividersWithAndWithoutScrollbar(double scaling)
    {
        foreach (var (width, rowCount) in new[] { (411, 1), (411, 100), (640, 100), (640, 1) })
        {
            var table = new PropertiesList
            {
                PropertyHeader = "Property",
                ValueHeader = "Value",
                PropertiesData = Enumerable.Range(0, rowCount)
                    .ToDictionary(index => $"Property {index}", _ => (object?)"Value")
            };
            var window = new Window { Width = width, Height = 426, Content = table };
            try
            {
                window.SetRenderScaling(scaling);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var headerLabel = Assert.Single(table.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == "Property");
                var headerCell = Assert.IsType<Border>(headerLabel.GetVisualParent());
                var scrollViewer = Assert.Single(table.GetVisualDescendants().OfType<ScrollViewer>());
                Assert.Equal(rowCount > 1, scrollViewer.Extent.Height > scrollViewer.Viewport.Height);
                var rows = table.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
                Assert.NotEmpty(rows);
                foreach (var row in rows)
                {
                    var cell = Assert.Single(row.GetVisualDescendants().OfType<Border>(),
                        border => border.BorderThickness == new Thickness(0, 0, 1, 1));
                    Assert.Equal(
                        headerCell.TranslatePoint(new Point(headerCell.Bounds.Width, 0), table)!.Value.X,
                        cell.TranslatePoint(new Point(cell.Bounds.Width, 0), table)!.Value.X);
                }
            }
            finally
            {
                window.Close();
            }
        }
    }

    [AvaloniaFact]
    public void CustomPropertiesTable_EditsValuesAndTabsInOrder()
    {
        var source = new PropertiesCollection(new Dictionary<string, object?> { ["Existing"] = 42 });
        var viewModel = new PropertiesDialogViewModel("key", "name", "icon", "", source);
        viewModel.ApplyCustomPropertyDefaults([
            new CustomPropertyDocument { Name = "Existing", DefaultValue = "ignored" },
            new CustomPropertyDocument { Name = "Second", DefaultValue = "default" }
        ]);
        var (window, table) = ShowCustomProperties(viewModel);
        try
        {
            var editors = table.GetVisualDescendants().OfType<TextBox>().ToArray();
            Assert.Equal(2, editors.Length);
            Assert.Equal("42", editors[0].Text);
            Assert.Equal("default", editors[1].Text);
            Assert.Equal(42, viewModel.GetProperties()["Existing"]);
            Assert.Contains(table.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Existing");
            Assert.Same(viewModel.CustomProperties, table.Rows);

            Assert.True(editors[0].Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new Thickness(1), editors[0].BorderThickness);
            editors[0].Text = "changed";
            Assert.Equal("changed", viewModel.CustomProperties[0].DefaultValue);
            Assert.Equal("changed", viewModel.GetProperties()["Existing"]);
            Assert.Equal(42, source["Existing"]);

            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "\t");
            Dispatcher.UIThread.RunJobs();
            Assert.Same(editors[1], window.FocusManager?.GetFocusedElement());
            Assert.Equal(new Thickness(0), editors[0].BorderThickness);
            var cancel = window.GetVisualDescendants().OfType<Button>().Single(button => button.IsCancel);
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(viewModel.DialogAccepted);
            Assert.Equal(42, source["Existing"]);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void PropertiesList_BindsFilteredRowsAndHidesEmptyTab()
    {
        var vm = new PropertiesDialogViewModel("key", "item", "icon", "note", new PropertiesCollection
        {
            ["nAmE"] = "item",
            ["RealDirectoryName"] = "directory",
            ["Comment"] = "note",
            ["Comments"] = "imported note",
            ["FullName"] = "/item"
        });
        var list = new PropertiesList();
        var tab = new TabItem { Content = list, DataContext = vm };
        using var listBinding = list.Bind(PropertiesList.PropertiesDataProperty, new Binding(nameof(vm.InfoProperties)));
        using var tabBinding = tab.Bind(TabItem.IsVisibleProperty, new Binding(nameof(vm.HasInfoTab)));
        var window = new Window { Content = tab };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(tab.IsVisible);
            Assert.Collection(list.PropertiesRows,
                row => { Assert.Equal("Comments", row.Key); Assert.Equal("imported note", row.Value); },
                row => { Assert.Equal("FullName", row.Key); Assert.Equal("/item", row.Value); });

            tab.DataContext = new PropertiesDialogViewModel("key", "item", "icon", "note", new PropertiesCollection
            {
                ["Name"] = "item",
                ["Comment"] = "note"
            });
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(list.PropertiesRows);
            Assert.False(tab.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void PropertiesList_DisplaysFormattedSizesAndPreservesOtherValues()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            var source = new PropertiesCollection { ["Size"] = "1536", ["FileSize"] = "1.5 MB", ["Attributes"] = "32" };
            var vm = new PropertiesDialogViewModel("key", "name", "icon", "", source);
            var list = new PropertiesList { PropertiesData = vm.InfoProperties };

            Assert.Contains(list.PropertiesRows, row => row.Key == "Size" && row.Value == "1.5 KB (1,536 bytes)");
            Assert.Contains(list.PropertiesRows, row => row.Key == "FileSize" && row.Value == "1.5 MB");
            Assert.Contains(list.PropertiesRows, row => row.Key == "Attributes" && row.Value == "Archive");
            Assert.Equal("1536", vm.GetProperties()["Size"]);
            Assert.Equal("32", vm.GetProperties()["Attributes"]);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [AvaloniaFact]
    public void CustomPropertiesTable_ScrollsAtMinimumSizeAndResizes()
    {
        var viewModel = new PropertiesDialogViewModel("key", "name", "icon", "", new PropertiesCollection());
        viewModel.ApplyCustomPropertyDefaults(Enumerable.Range(0, 40).Select(index =>
            new CustomPropertyDocument { Name = $"Property {index}", DefaultValue = $"Value {index}" }));
        var (window, table) = ShowCustomProperties(viewModel);
        try
        {
            var list = table.GetVisualDescendants().OfType<ListBox>().Single();
            var scroller = list.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.Equal(411, window.Bounds.Width);
            Assert.Equal(560, window.Bounds.Height);
            Assert.True(scroller.Extent.Height > scroller.Viewport.Height);
            Assert.True(scroller.Extent.Width <= scroller.Viewport.Width);
            Assert.True(table.Bounds.Width < window.Bounds.Width);
            Assert.True(table.Bounds.Height < window.Bounds.Height);

            var firstEditor = table.GetVisualDescendants().OfType<TextBox>()
                .Single(editor => ReferenceEquals(editor.DataContext, viewModel.CustomProperties[0]));
            Assert.True(firstEditor.Focus());
            for (var index = 1; index < viewModel.CustomProperties.Count; index++)
            {
                window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "\t");
                Dispatcher.UIThread.RunJobs();
                var focusedEditor = Assert.IsType<TextBox>(window.FocusManager?.GetFocusedElement());
                Assert.Same(viewModel.CustomProperties[index], focusedEditor.DataContext);
            }

            Assert.True(scroller.Offset.Y > 0);
            window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, "\t");
            Dispatcher.UIThread.RunJobs();
            Assert.Same(viewModel.CustomProperties[38], Assert.IsType<TextBox>(window.FocusManager?.GetFocusedElement()).DataContext);

            list.ScrollIntoView(viewModel.CustomProperties[39]);
            Dispatcher.UIThread.RunJobs();
            var lastEditor = table.GetVisualDescendants().OfType<TextBox>()
                .Single(editor => ReferenceEquals(editor.DataContext, viewModel.CustomProperties[39]));
            lastEditor.Text = new string('x', 300);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(lastEditor.Text, viewModel.GetProperties()["Property 39"]);
            Assert.True(scroller.Extent.Width <= scroller.Viewport.Width);

            window.Width = 700;
            window.Height = 600;
            Dispatcher.UIThread.RunJobs();
            Assert.True(table.Bounds.Width > 400);
            Assert.True(scroller.Extent.Width <= scroller.Viewport.Width);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CustomPropertiesTable_LocalizesHeadersAndHidesWhenEmpty()
    {
        var previousCulture = LocalizationService.Current.CurrentCulture;
        var viewModel = new PropertiesDialogViewModel("key", "name", "icon", "", new PropertiesCollection());
        Assert.False(viewModel.HasCustomProperties);
        viewModel.ApplyCustomPropertyDefaults([new CustomPropertyDocument { Name = "Name" }]);
        var (window, table) = ShowCustomProperties(viewModel);
        try
        {
            LocalizationService.Current.SetCulture(CultureInfo.GetCultureInfo("en"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(LocalizationService.Current[UiKeys.Property], table.PropertyHeader);
            Assert.Equal(LocalizationService.Current[UiKeys.Value], table.ValueHeader);
            var englishHeader = table.PropertyHeader;

            LocalizationService.Current.SetCulture(CultureInfo.GetCultureInfo("lt"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(LocalizationService.Current[UiKeys.Property], table.PropertyHeader);
            Assert.Equal(LocalizationService.Current[UiKeys.Value], table.ValueHeader);
            Assert.NotEqual(englishHeader, table.PropertyHeader);
        }
        finally
        {
            window.Close();
            LocalizationService.Current.SetCulture(previousCulture);
        }
    }

    [AvaloniaFact]
    public void PropertiesTable_RemainsReadOnlyAndCustomTabFollowsDefaults()
    {
        var source = new PropertiesCollection(new Dictionary<string, object?> { ["Format"] = "Audio" });
        var viewModel = new PropertiesDialogViewModel("key", "name", "icon", "", source);
        var window = new PropertiesWindow { DataContext = viewModel };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
            var customTab = Assert.IsType<TabItem>(tabs.Items[3]);
            Assert.False(customTab.IsVisible);

            tabs.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            var table = Assert.IsType<PropertiesList>(Assert.IsType<TabItem>(tabs.Items[1]).Content);
            Assert.Contains(table.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Format");
            Assert.Contains(table.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Audio");
            Assert.Empty(table.GetVisualDescendants().OfType<TextBox>());
            Assert.True(table.GetVisualDescendants().OfType<ListBoxItem>().Single().IsTabStop);

            viewModel.ApplyCustomPropertyDefaults([new CustomPropertyDocument { Name = "Custom" }]);
            Dispatcher.UIThread.RunJobs();
            Assert.True(customTab.IsVisible);
            viewModel.ApplyCustomPropertyDefaults([]);
            Dispatcher.UIThread.RunJobs();
            Assert.False(customTab.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CustomPropertiesTable_ConfirmRetainsUnchangedValues()
    {
        var source = new PropertiesCollection(new Dictionary<string, object?> { ["Number"] = 42 });
        var viewModel = new PropertiesDialogViewModel("key", "name", "icon", "", source);
        viewModel.ApplyCustomPropertyDefaults([
            new CustomPropertyDocument { Name = "Number" },
            new CustomPropertyDocument { Name = "New", DefaultValue = "default" }
        ]);
        var (window, table) = ShowCustomProperties(viewModel);
        try
        {
            var editor = table.GetVisualDescendants().OfType<TextBox>()
                .Single(value => ReferenceEquals(value.DataContext, viewModel.CustomProperties[1]));
            editor.Text = "edited";
            viewModel.ConfirmCommand.Execute(null);
            Assert.True(viewModel.DialogAccepted);
            Assert.Equal(42, viewModel.GetProperties()["Number"]);
            Assert.Equal("edited", viewModel.GetProperties()["New"]);
            Assert.Equal(42, source["Number"]);
            Assert.False(source.ContainsKey("New"));
        }
        finally
        {
            window.Close();
        }
    }

    private static (PropertiesWindow Window, PropertiesList Table) ShowCustomProperties(PropertiesDialogViewModel viewModel)
    {
        var window = new PropertiesWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
        tabs.SelectedIndex = 3;
        Dispatcher.UIThread.RunJobs();
        var table = Assert.IsType<PropertiesList>(Assert.IsType<TabItem>(tabs.Items[3]).Content);
        return (window, table);
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
    public void BrowserViewMode_EnumValues_AreStable()
    {
        Assert.Equal(0, (int)BrowserViewMode.Tiles);
        Assert.Equal(1, (int)BrowserViewMode.SmallIcons);
        Assert.Equal(2, (int)BrowserViewMode.LargeIcons);
        Assert.Equal(3, (int)BrowserViewMode.List);
        Assert.Equal(4, (int)BrowserViewMode.Details);
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
    public void StatusBar_LayoutStructure_MatchesCompactLegacy22pxSpecification()
    {
        var statusBar = new StatusBar();

        Assert.Equal(22, statusBar.Height);
        Assert.Equal(22, statusBar.MinHeight);

        var border = Assert.IsType<Border>(statusBar.Content);
        Assert.Equal(new Avalonia.Thickness(0, 1, 0, 0), border.BorderThickness);
        Assert.Equal(new Avalonia.Thickness(4, 0), border.Padding);
        Assert.Equal(22, border.Height);

        var grid = Assert.IsType<Grid>(border.Child);
        Assert.Equal(4, grid.ColumnDefinitions.Count);
        Assert.Equal(GridUnitType.Star, grid.ColumnDefinitions[0].Width.GridUnitType);
        Assert.Equal(GridUnitType.Auto, grid.ColumnDefinitions[1].Width.GridUnitType);
        Assert.Equal(GridUnitType.Auto, grid.ColumnDefinitions[2].Width.GridUnitType);
        Assert.Equal(GridUnitType.Auto, grid.ColumnDefinitions[3].Width.GridUnitType);

        var progressBar = Assert.IsType<ProgressBar>(grid.Children[2]);
        Assert.Equal(100, progressBar.Width);
        Assert.Equal(14, progressBar.Height);
        var drawingBrush = Assert.IsType<Avalonia.Media.DrawingBrush>(progressBar.Foreground);
        Assert.Equal(Avalonia.Media.TileMode.Tile, drawingBrush.TileMode);
    }

    [AvaloniaFact]
    public void ClassicToolbar_LayoutStructure_MatchesCompactLegacy25pxSpecification()
    {
        var toolbar = new ClassicToolbar();

        Assert.Equal(25, toolbar.Height);
        Assert.Equal(25, toolbar.MinHeight);

        var border = Assert.IsType<Border>(toolbar.Content);
        Assert.Equal(new Avalonia.Thickness(0, 0, 0, 1), border.BorderThickness);
        Assert.Equal(new Avalonia.Thickness(2, 1), border.Padding);
        Assert.Equal(25, border.Height);

        var dockPanel = Assert.IsType<DockPanel>(border.Child);
        var grip = Assert.IsType<Avalonia.Controls.Shapes.Rectangle>(dockPanel.Children[0]);
        Assert.Equal(3, grip.Width);
        Assert.Equal(14, grip.Height);
        var drawingBrush = Assert.IsType<Avalonia.Media.DrawingBrush>(grip.Fill);
        Assert.Equal(Avalonia.Media.TileMode.Tile, drawingBrush.TileMode);

        var itemsControl = Assert.IsType<ItemsControl>(dockPanel.Children[1]);
        Assert.Same(toolbar.Items, itemsControl.ItemsSource);
    }

    [Theory]
    [InlineData("&New", "_New")]
    [InlineData("&Open", "_Open")]
    [InlineData("Į&rašyti", "Į_rašyti")]
    [InlineData("_New", "_New")]
    [InlineData("&&Save", "&Save")]
    [InlineData("File", "File")]
    [InlineData(null, null)]
    public void ClassicToolbarButton_MnemonicNormalization_NormalizesAmpersandAndUnderscore(string? input, string? expected)
    {
        var button = new ClassicToolbarButton { Text = input };
        Assert.Equal(expected, button.Text);
    }

    [AvaloniaFact]
    public void ClassicToolbarButton_ExecutesBoundCommand()
    {
        var executed = false;
        var command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => executed = true);
        var button = new ClassicToolbarButton { Command = command };

        Assert.True(button.IsEnabled);
        button.Command.Execute(null);
        Assert.True(executed);
    }
}
