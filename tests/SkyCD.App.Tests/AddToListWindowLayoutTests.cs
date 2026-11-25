using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #942: the Add To List dialog must give every section its own sizing row. The source panel
/// previously lived in a fixed 6 px row and drew over the name/Location block, and the overflowing
/// content pushed the validation line out of the fixed 445x480 window.
/// </summary>
public class AddToListWindowLayoutTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";
    private static readonly XNamespace IconPacks = "https://github.com/MahApps/IconPacks.Avalonia";
    private static readonly XNamespace Xaml2006 = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void FormFields_HaveNoFixedPixelSpacerRow()
    {
        var form = GetFormFieldsGrid();

        Assert.DoesNotContain(((string?)form.Attribute("RowDefinitions") ?? string.Empty).Split(','),
            definition => double.TryParse(definition, out _));
        Assert.Equal("{DynamicResource SkySpaceXs}", (string?)form.Attribute("RowSpacing"));
    }

    [Fact]
    public void EachSection_OwnsASizingRowInsideTheFormGrid()
    {
        var form = GetFormFieldsGrid();

        // Auto rows only: no fixed pixel row can clip a section again.
        Assert.Equal("Auto,Auto", (string?)form.Attribute("RowDefinitions"));

        var nameBlock = GetNameBlock(form);
        var sourceRegion = GetSourceRegion(form);
        var column = GetContentColumn();
        var destinationSection = GetDestinationSection(column);

        // The name/Location block, the source panel and the destination choices each own a row.
        Assert.Equal("0", GridRowOf(nameBlock));
        Assert.Equal("1", GridRowOf(sourceRegion));
        Assert.Equal("2", (string?)destinationSection.Attribute("Grid.Row"));
        Assert.Contains("UiKeys.SelectMediaName", nameBlock.ToString(), StringComparison.Ordinal);
        Assert.Contains("UiKeys.Location", nameBlock.ToString(), StringComparison.Ordinal);
        Assert.Contains(destinationSection.Descendants(Avalonia + "Button"),
            button => (string?)button.Attribute("Command") == "{Binding ConfirmCommand}");
    }

    [Fact]
    public void ContentColumn_KeepsTheActionsAnchoredToTheBottom()
    {
        var column = GetContentColumn();

        // The star row between the fields and the action row absorbs the fixed window height, so the
        // actions stay pinned to the bottom exactly as #823/#826 established.
        Assert.Equal("Auto,*,Auto", (string?)column.Attribute("RowDefinitions"));
        Assert.Equal("2", (string?)GetDestinationSection(column).Attribute("Grid.Row"));
        Assert.Single(column.Elements(Avalonia + "Grid"),
            grid => (string?)grid.Attribute(Xaml2006 + "Name") == "FormFieldsGrid");
    }

    [Fact]
    public void DestinationRadios_RenderDirectlyWithoutATabStrip()
    {
        var column = GetContentColumn();
        var destinationSection = GetDestinationSection(column);

        Assert.DoesNotContain(column.Descendants(Avalonia + "TabControl"), _ => true);
        Assert.DoesNotContain(column.Descendants(Avalonia + "TabItem"), _ => true);
        Assert.DoesNotContain(column.Elements(Avalonia + "Grid"),
            grid => (string?)grid.Attribute(Xaml2006 + "Name") == "SourceTabHost");

        var radios = destinationSection.Descendants(Avalonia + "RadioButton").ToList();
        Assert.Equal(2, radios.Count);
        Assert.Contains(radios, radio => (string?)radio.Attribute("CommandParameter") == "NewMedia");
        Assert.Contains(radios, radio => (string?)radio.Attribute("CommandParameter") == "SelectedFolder");
    }

    [Fact]
    public void SourcePanel_ScrollsInsteadOfOverflowingTheNameBlock()
    {
        var form = GetFormFieldsGrid();
        var viewer = GetSourceRegion(form);

        Assert.Equal("ScrollViewer", viewer.Name.LocalName);
        Assert.Equal("1", (string?)viewer.Attribute("Grid.Row"));
        Assert.Equal("{DynamicResource SkyFormFieldsMaxHeight}", (string?)viewer.Attribute("MaxHeight"));
        Assert.Equal("Auto", (string?)viewer.Attribute("VerticalScrollBarVisibility"));
        Assert.Equal("Disabled", (string?)viewer.Attribute("HorizontalScrollBarVisibility"));

        // Every source-specific tab keeps its own layout inside the scrolling region.
        var details = Assert.Single(viewer.Elements(Avalonia + "Grid"));
        Assert.Equal("Top", (string?)details.Attribute("VerticalAlignment"));

        foreach (var source in new[]
                 {
                     "{Binding IsSourceMedia}",
                     "{Binding IsSourceFolder}",
                     "{Binding IsSourceInternet}",
                     "{Binding IsSourceTitleOnly}"
                 })
        {
            Assert.Contains(details.Elements(),
                element => (string?)element.Attribute("IsVisible") == source);
        }

        // The Location picker stays outside the scrolling region, above it.
        Assert.DoesNotContain(viewer.Descendants(Avalonia + "ComboBox"),
            comboBox => comboBox.Attribute("SelectedItem")?.Value == "{Binding SelectedLocation}");
    }

    [Fact]
    public void ValidationMessage_StaysInItsOwnRootRowBelowTheActions()
    {
        var root = GetRootGrid();
        var message = GetValidationMessage(root);

        Assert.Equal("1", (string?)message.Attribute("Grid.Row"));
        Assert.Equal("2", (string?)message.Attribute("Grid.ColumnSpan"));
        Assert.Contains("ValidationMessage", (string?)message.Attribute("Text"), StringComparison.Ordinal);

        var contentColumn = Assert.Single(root.Elements(Avalonia + "Grid"),
            grid => (string?)grid.Attribute(Xaml2006 + "Name") == "ContentColumn");
        Assert.Equal("Auto,*,Auto", (string?)contentColumn.Attribute("RowDefinitions"));

        var messageIndex = root.Elements().ToList().IndexOf(message);
        var contentIndex = root.Elements().ToList().IndexOf(contentColumn);
        Assert.True(contentIndex >= 0 && contentIndex < messageIndex,
            "the validation line must come after the content so the Auto row below it is never clipped.");
    }

    [Fact]
    public void SourceSelector_UsesDistinctColoredArtwork()
    {
        // Issue #1099: every source mode shows its own colored artwork from the shared Twemoji pipeline,
        // instead of the monochrome line glyphs introduced with #1061.
        var document = XDocument.Parse(ReadRepoFile("src", "SkyCD.App", "Views", "AddToListWindow.axaml"));
        var buttons = document.Descendants(Avalonia + "Button")
            .Where(button => ((string?)button.Attribute("Classes") ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("source"))
            .ToList();

        Assert.Equal(4, buttons.Count);
        Assert.DoesNotContain(document.Descendants(IconPacks + "PackIconControl"),
            icon => icon.Ancestors(Avalonia + "Button")
                .Any(button => ((string?)button.Attribute("Classes") ?? string.Empty).Contains("source")));

        var images = buttons
            .Select(button => Assert.Single(button.Descendants(Avalonia + "Image")))
            .ToList();
        var sources = images.Select(image => (string?)image.Attribute("Source")).ToList();

        Assert.Equal(
            ["{x:Static icons:SourceIcons.Media}",
             "{x:Static icons:SourceIcons.Folder}",
             "{x:Static icons:SourceIcons.Internet}",
             "{x:Static icons:SourceIcons.TitleOnly}"],
            sources);
        Assert.All(images, image =>
        {
            Assert.Equal("40", (string?)image.Attribute("Width"));
            Assert.Equal("40", (string?)image.Attribute("Height"));
        });
    }

    [Fact]
    public void Layout_UsesDesignTokensForSpacing()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "AddToListWindow.axaml");

        foreach (var token in new[]
                 {
                     "{DynamicResource SkySpaceXs}",
                     "{DynamicResource SkySpaceSm}",
                     "{DynamicResource SkyInsetXxs}",
                     "{DynamicResource SkyWindowPadding}"
                 })
        {
            Assert.Contains(token, xaml, StringComparison.Ordinal);
        }
    }

    private static XElement GetFormFieldsGrid() => Assert.Single(GetRootGrid().Descendants(Avalonia + "Grid"),
        grid => (string?)grid.Attribute(Xaml2006 + "Name") == "FormFieldsGrid");

    private static XElement GetContentColumn() => Assert.Single(GetRootGrid().Elements(Avalonia + "Grid"),
        grid => (string?)grid.Attribute(Xaml2006 + "Name") == "ContentColumn");

    private static XElement GetDestinationSection(XElement column) =>
        Assert.Single(column.Elements(Avalonia + "Grid"),
            grid => (string?)grid.Attribute(Xaml2006 + "Name") == "DestinationSection");

    private static XElement GetNameBlock(XElement form) =>
        Assert.Single(form.Elements(Avalonia + "StackPanel"));

    private static XElement GetSourceRegion(XElement form) =>
        Assert.Single(form.Elements(Avalonia + "ScrollViewer"));

    private static XElement GetValidationMessage(XElement root) =>
        Assert.Single(root.Elements(Avalonia + "TextBlock"),
            textBlock => (string?)textBlock.Attribute(Xaml2006 + "Name") == "ValidationMessageText");

    private static XElement GetRootGrid()
    {
        var document = XDocument.Parse(ReadRepoFile("src", "SkyCD.App", "Views", "AddToListWindow.axaml"));
        return Assert.Single(document.Root!.Elements(Avalonia + "Grid"));
    }

    private static string? GridRowOf(XElement element) => (string?)element.Attribute("Grid.Row") ?? "0";

    private static string ReadRepoFile(params string[] parts)
    {
        var fullPath = Path.Combine([FindRepoRoot(), .. parts]);
        return File.ReadAllText(fullPath);
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
