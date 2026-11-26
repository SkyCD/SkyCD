using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #1002: the Advanced find window owns one section per row, spaced with design tokens.
/// The fixed 170 px filter row is gone, the filter and result lists share the remaining height,
/// saved searches sit in a separated band above the dialog actions, and no literal margins remain.
/// </summary>
public sealed class AdvancedCatalogFindWindowLayoutTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";
    private static readonly XNamespace Xaml2006 = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly Regex NonZeroMargin = new("\\bMargin=\"[^\"]*[1-9]", RegexOptions.Compiled);
    private static readonly Regex LiteralPadding = new("\\bPadding=\"[0-9]", RegexOptions.Compiled);

    [Fact]
    public void SectionsOwnSizingRowsAndTheListsShareTheRemainingHeight()
    {
        var root = GetRootGrid();
        var definitions = ((string?)root.Attribute("RowDefinitions") ?? string.Empty).Split(',');

        // No fixed pixel row can clip a list again, and the two lists split the spare height 2:3.
        Assert.DoesNotContain(definitions, definition => double.TryParse(definition, out _));
        Assert.Equal("Auto,Auto,Auto,2*,Auto,3*,Auto,Auto,Auto", (string?)root.Attribute("RowDefinitions"));
        Assert.Equal("{DynamicResource SkySpaceSm}", (string?)root.Attribute("RowSpacing"));
        Assert.Equal("{DynamicResource SkyWindowPadding}", (string?)root.Attribute("Margin"));
    }

    [Fact]
    public void SavedSearchesSitAboveTheDialogActions()
    {
        var root = GetRootGrid();
        var rows = root.Elements()
            .Select(element => ((string?)element.Attribute("Grid.Row") ?? "0") + ":" + element.Name.LocalName)
            .ToList();

        var savedSearchIndex = rows.IndexOf("7:Border");
        var actionsIndex = rows.IndexOf("8:Grid");
        Assert.True(savedSearchIndex >= 0, "the saved-search band must own its own row");
        Assert.True(actionsIndex > savedSearchIndex, "Open/Close must be the final row below the saved searches");

        var band = root.Elements(Avalonia + "Border").Single();
        Assert.Equal("{Binding CanSaveSearch}", (string?)band.Attribute("IsVisible"));
        Assert.Equal("{DynamicResource ThemeBorderMidBrush}", (string?)band.Attribute("BorderBrush"));
        Assert.Equal("0,1,0,0", (string?)band.Attribute("BorderThickness"));
        Assert.Equal("{DynamicResource SkyInsetSm}", (string?)band.Attribute("Padding"));

        var actions = root.Elements(Avalonia + "Grid").Single(grid => (string?)grid.Attribute("Grid.Row") == "8");
        Assert.Equal("*,Auto,Auto", (string?)actions.Attribute("ColumnDefinitions"));
        Assert.Contains(actions.Descendants(Avalonia + "Button"),
            button => (string?)button.Attribute("IsCancel") == "True");
        Assert.Contains(actions.Descendants(Avalonia + "Button"),
            button => (string?)button.Attribute("Click") == "OnOpenResultClicked");
    }

    [Fact]
    public void ToolbarGroupsFilterAndRunActionsApartWithTokenSpacing()
    {
        var toolbar = GetRootGrid().Elements(Avalonia + "Grid")
            .Single(grid => (string?)grid.Attribute("Grid.Row") == "1");
        var groups = toolbar.Elements(Avalonia + "WrapPanel").ToList();

        Assert.Equal(2, groups.Count);
        Assert.Equal("{DynamicResource SkySpaceSm}", (string?)groups[0].Attribute("ItemSpacing"));

        // A wider token separates the filter-editing group from the run group.
        Assert.Equal("{DynamicResource SkySpaceMd}", (string?)groups[1].Attribute("ItemSpacing"));
        Assert.Equal("{DynamicResource SkySpaceSm}", (string?)groups[1].Attribute("LineSpacing"));

        // One nested group holds the filter-editing actions, the other the run actions.
        var filterGroup = Assert.Single(groups[1].Elements(Avalonia + "StackPanel"),
            panel => panel.ToString().Contains("AddFilterCommand", StringComparison.Ordinal));
        var runGroup = Assert.Single(groups[1].Elements(Avalonia + "StackPanel"),
            panel => panel.ToString().Contains("SearchCommand", StringComparison.Ordinal));

        Assert.Contains("ClearFiltersCommand", filterGroup.ToString(), StringComparison.Ordinal);
        Assert.Contains("ResetFiltersCommand", filterGroup.ToString(), StringComparison.Ordinal);
        Assert.Null(runGroup.Descendants(Avalonia + "Button")
            .FirstOrDefault(button => ((string?)button.Attribute("Command"))?.Contains("AddFilter", StringComparison.Ordinal) == true));
        Assert.Contains("CancelSearchCommand", runGroup.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ScopeRowAndSavedSearchStripFlowWithTokenSpacing()
    {
        var root = GetRootGrid();
        var scopeRow = Assert.Single(root.Elements(Avalonia + "StackPanel"),
            panel => panel.Attribute("Grid.Row") is null);
        Assert.Equal("Horizontal", (string?)scopeRow.Attribute("Orientation"));
        Assert.Equal("{DynamicResource SkySpaceSm}", (string?)scopeRow.Attribute("Spacing"));
        Assert.Equal(2, scopeRow.Elements(Avalonia + "CheckBox").Count());

        var strip = root.Elements(Avalonia + "Border").Single().Elements(Avalonia + "WrapPanel").Single();
        Assert.Equal("{DynamicResource SkySpaceSm}", (string?)strip.Attribute("ItemSpacing"));
        Assert.Equal("{DynamicResource SkySpaceXs}", (string?)strip.Attribute("LineSpacing"));
    }

    [Fact]
    public void FilterRowsShareTheHeaderColumnWidths()
    {
        var root = GetRootGrid();
        var header = root.Elements(Avalonia + "Grid").Single(grid => (string?)grid.Attribute("Grid.Row") == "2");
        var filterRow = root.Descendants(Avalonia + "Grid").Single(grid =>
            grid.Ancestors(Avalonia + "ListBox").Any()
            && grid.Elements(Avalonia + "Grid.ColumnDefinitions").Any());

        Assert.Equal(FirstColumnWidth(header), FirstColumnWidth(filterRow));
        Assert.Equal("{StaticResource SkyAdvancedFindLabelWidth}", FirstColumnWidth(filterRow));
        Assert.Equal(RemainingColumnWidths(header), RemainingColumnWidths(filterRow));
        Assert.Equal("{DynamicResource SkySpaceSm}", (string?)header.Attribute("ColumnSpacing"));
        Assert.Equal("{DynamicResource SkySpaceSm}", (string?)filterRow.Attribute("ColumnSpacing"));
    }

    private static string? FirstColumnWidth(XElement grid) =>
        grid.Elements(Avalonia + "Grid.ColumnDefinitions").Elements(Avalonia + "ColumnDefinition")
            .Select(column => (string?)column.Attribute("Width")).FirstOrDefault();

    private static string RemainingColumnWidths(XElement grid) =>
        string.Join(",", grid.Elements(Avalonia + "Grid.ColumnDefinitions").Elements(Avalonia + "ColumnDefinition")
            .Select(column => (string?)column.Attribute("Width")).Skip(1));

    [Fact]
    public void ResultRowsUsePaddingTokensInsteadOfLiteralMargins()
    {
        var results = GetRootGrid().Descendants(Avalonia + "ListBox")
            .Single(listBox => (string?)listBox.Attribute(Xaml2006 + "Name") == "ResultsList");
        var row = Assert.Single(results.Descendants(Avalonia + "Grid"));
        var padding = Assert.IsType<XElement>(row.Parent);

        Assert.Null(row.Attribute("Margin"));
        Assert.Equal("Border", padding.Name.LocalName);
        Assert.Equal("{DynamicResource SkyInsetXxs}", (string?)padding.Attribute("Padding"));
        Assert.Equal("{DynamicResource SkySpaceMd}", (string?)row.Attribute("ColumnSpacing"));
    }

    [Fact]
    public void WindowDoesNotDeclareLiteralMarginsOrPaddings()
    {
        var xaml = ReadWindowXaml();

        Assert.DoesNotMatch(NonZeroMargin, xaml);
        Assert.DoesNotMatch(LiteralPadding, xaml);
    }

    private static XElement GetRootGrid() => Assert.Single(XDocument.Parse(ReadWindowXaml()).Root!.Elements(Avalonia + "Grid"));

    private static string ReadWindowXaml()
    {
        var path = Path.Combine(FindRepoRoot(), "src", "SkyCD.App", "Views", "AdvancedCatalogFindWindow.axaml");
        return File.ReadAllText(path);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SkyCD.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root with SkyCD.slnx was not found.");
    }
}
